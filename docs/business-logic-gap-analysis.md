# AADHI CRACKERS — Business Logic Gap Analysis (Phase 0 Report)

**Date:** 2026-09-01
**Repos:** `Aadhi_API_live` (.NET 10 backend), `Aadhi_UI_live` (admin-web + customer-web)
**Method:** Fresh line-by-line inspection of current code by four parallel audits (inventory/orders/payments, catalog/purchasing/finance, platform, frontend). No prior "COMPLETE" claims were trusted. **No code was changed — this is a report only.**

Classifications: **CORRECT** · **INCORRECT** · **PARTIAL** · **DUPLICATED** · **MISSING**

---

## VERDICT SUMMARY

| Domain | Classification | One-line reality |
|---|---|---|
| Stock source of truth | **DUPLICATED** | Product AND StockItem both hold stock; drift silently |
| Order stock lifecycle | **PARTIAL** | Reserve/ship/cancel math right; ledger lies; no concurrency safety |
| Stock movements ledger | **INCORRECT** | No reservation types; Before==After rows; sales double-counted |
| Order state machine | **PARTIAL** | 409 on invalid transitions works; MoveToPacking silently no-ops |
| Payments | **PARTIAL** | No UTR uniqueness, no idempotency key; partial payment marks order Paid |
| Refunds | **MISSING** | No entity, no endpoint, nothing |
| Returns | **MISSING** | No entity/workflow; `Returned` status auto-restocks with zero inspection |
| Purchase approval | **MISSING** | POs born `Submitted`; no approval endpoint; `Approved` unreachable |
| Goods receipt | **INCORRECT** | Total delivered → sellable stock; no rejected/damaged split; no over-receive guard; can credit wrong product |
| Supplier bills / payables | **MISSING** | Zero code anywhere |
| COGS | **INCORRECT** | Hardcoded `UnitPrice * 0.65m`; no CostPriceAtSale |
| P&L | **INCORRECT** | Revenue includes unpaid orders + tax + shipping; no Net Sales line |
| Reports/dashboard | **INCORRECT** | ~25 fabricated-number sites; 2 endpoints 100% hardcoded, never query DB |
| ProductCategory junction | **DUPLICATED (dead)** | Table exists, never wired; single CategoryId FK is live |
| Variants / Bundles | **MISSING (dead scaffolding)** | ProductVariant + GiftBoxItem entities exist, zero logic; gift boxes matched by name strings |
| Promotions | **PARTIAL** | PerCustomerLimit never enforced; UsedCount racy; no cancel rollback; no CRUD endpoints |
| Invoices | **PARTIAL** | Totals snapshotted; customer/address/lines live-referenced; cancelled orders keep open invoices |
| Business numbering | **INCORRECT** | All 7 series = `CountAsync()+1` (race + soft-delete collision); PAY has a 2nd random generator |
| Audit | **PARTIAL** | Persists, but failures swallowed silently; RejectPayment/MoveToPacking unaudited; ChangedFields always null |
| Outbox | **INCORRECT** | `OrderPlaced`/`OrderStatusChanged` never saved to DB; worker only sees no-op AuditLogCreated; no backoff/dead-letter |
| Transactions | **MISSING** | Zero `BeginTransaction` in codebase; interface can't even open one |
| Permissions | **MISSING** | JWT carries permission claims; no policy ever checks them |
| IDOR | **MISSING** | Any authenticated customer reads ALL orders; anonymous tracking matches phone numbers → PII enumeration |
| FluentValidation | **MISSING (execution)** | 8 validators registered, zero ever run |
| Elasticsearch | **MISSING** | No client, no package; comments are aspirational |
| Rate-limit logging | **MISSING** | Rejections never persisted; log endpoint permanently empty; values hardcoded |
| Migrations | **INCORRECT** | `EnsureCreated` only; no Migrations folder; schema can't evolve |
| Concurrency tokens | **MISSING** | RowVersion declared, never configured; stock is last-writer-wins |
| Admin UI wiring | **PARTIAL** | ~15 API functions are hard stubs; 4 wired write-paths broken by contract mismatch |
| Customer UI | **PARTIAL** | Cart math fixed; checkout address, mobile confirmation, order history, base URL still broken |

---

## 1. STOCK & INVENTORY

### 1.1 Source of truth — DUPLICATED
- `Product.StockQuantity` / `Product.ReservedQuantity` (`Domain\Entities\CatalogEntities.cs:77-78`) **and** `StockItem.QuantityOnHand` / `QuantityReserved` (`Domain\Entities\InventoryEntities.cs:30-31`) are both live authorities.
- Product mutation sites: `OrderService.cs:117, 279, 318-319, 359, 654`; `InventoryService.cs:150`; `PurchaseService.cs:260`; `CatalogService.cs:439`; seeder.
- StockItem mutation sites: `OrderService.cs:127, 288, 327-328, 367, 663`; `InventoryService.cs:149, 228, 231`; `PurchaseService.cs:266, 270-277`; `CatalogService.cs:471-478`; seeder.
- StockItem updates are **conditional** (`if (warehouse != null)` / `if (stockItem != null)` — `OrderService.cs:120-129, 282-289, 321-329, 361-368`): a missing row silently mutates Product alone. `Math.Max(0,…)` clamps (`OrderService.cs:279,318-319,327-328,654,663`; `InventoryService.cs:150`) let the ledgers drift by different amounts, permanently masked.
- `Available = OnHand − Reserved` formula is consistent (`CatalogEntities.cs:96`, `InventoryEntities.cs:34`) but clamped at 0, hiding oversold states.

### 1.2 Order lifecycle stock effects
- **Creation — PARTIAL.** Validates then reserves correctly (`OrderService.cs:111-117,127`: Reserved += qty, OnHand unchanged). But **no StockReserved movement type exists** — the reservation is logged as a `Sale` with `QuantityBefore == QuantityAfter` and `Change = -qty` (`OrderService.cs:163-167`).
- **Race protection — MISSING.** No `BeginTransaction`/`TransactionScope` anywhere in `src` (grep: zero). `Product.RowVersion` declared (`CatalogEntities.cs:89`) but never configured (`AadhiDbContext.cs` Product block `:60-87` has no `IsRowVersion()`); StockItem has none. Check-then-reserve is racy → oversell.
- **Cancellation — CORRECT** semantics (`OrderService.cs:269-289`: Reserved release only). Double-cancel blocked: `OrderEntities.cs:94` (`if (OrderStatus == nextStatus) return false;`) + `:105` (`Cancelled => false`) → 409. Movement mislabeled `Adjustment` with Before==After (`:291-302`).
- **Shipment — CORRECT (deducts exactly once).** Guard at `OrderService.cs:306`:
  `else if (request.NewStatus is OrderStatus.Shipped or OrderStatus.Delivered && oldStatus is not (OrderStatus.Shipped or OrderStatus.OutForDelivery or OrderStatus.Delivered))`
  OutForDelivery→Delivered does NOT double-deduct. But it writes a **second `Sale` movement** (`:335`) for units already logged as `Sale` at reservation → ledger double-counts sales.
- **Returned — INCORRECT.** `OrderService.cs:347-385` auto-restocks immediately (`StockQuantity += qty` at `:359`, StockItem `:367`) as type `Adjustment`. No inspection, no sellable/damaged split. `StockMovementType.Return` and `Damage` are never used anywhere.
- **State machine — PARTIAL.** Invalid transitions → `InvalidOrderStateTransitionException` → 409 (`OrderEntities.cs:113-116`, `ProblemDetailsExceptionHandler.cs:42-47`); history rows per transition (`OrderEntities.cs:123-131`). Gap: `MoveToPackingAsync` (`OrderService.cs:615-622`) silently no-ops (200) on wrong status — no rejection, no history, no audit.

### 1.3 Adjustment — PARTIAL
- Reason required ✔ (`InventoryService.cs:109-110`); isRelative semantics correct ✔ (`:135-147`); StockItem + Product + movement in one `SaveChangesAsync` ✔ (`:166-168`).
- Audit is a separate, failure-swallowed save after commit (`AuditLogService.cs:114-121`). No business outbox event. `Math.Max(0, product.StockQuantity + delta)` (`:150`) can apply a smaller delta to Product than StockItem → drift.

### 1.4 Transfer — PARTIAL
- Atomic (single save `:262`) ✔, dual TransferOut/TransferIn movements ✔ (`:234-260`), available-stock guard ✔ (`:210-211`).
- **No approval workflow** — immediate execution; no Draft/Submitted/Approved/InTransit/Received states exist for transfers.

### 1.5 Movements ledger — INCORRECT
- Enum (`DomainEnums.cs:49-59`): has OpeningStock, Purchase, Sale, Return, Adjustment, TransferIn, TransferOut, Damage. **Missing: StockReserved, StockReservationReleased.** Return/Damage: zero usages.
- Before==After rows with nonzero Change at `OrderService.cs:164-166, 296-298, 671-673`. Double Sale counting (`:163` + `:335`). Mixed reference frames: adjustment Before/After = StockItem (`InventoryService.cs:159-160`), GRN = Product (`PurchaseService.cs:287-288`), ship = Product (`OrderService.cs:337-338`) with a WarehouseId on the row. **The ledger cannot be replayed to reconstruct balances.**

---

## 2. PRODUCT / CATEGORY MODEL

### 2.1 Product — PARTIAL
- Present: SKU `:62`, Slug `:64`, BrandId `:69`, Unit `:82`, CostPrice `:73`, TaxRate `:74`, SafetyInformation `:88` (all `CatalogEntities.cs`). Renamed: SellingPrice→`Price` `:71`, MRP→`CompareAtPrice` `:72`. Discount split into DiscountType/DiscountValue `:75-76`.
- **`ProductType` — MISSING** entirely. Gift box/combo detection is **name string matching**: `CatalogService.cs:641` (`Category.Name.Contains("gift box") || Name.Contains("gift box")`), `:652` (`Contains("combo") … || DiscountValue > 20` — any product with >₹20 flat discount is a "combo").

### 2.2 ProductCategory junction — DUPLICATED (dead code)
- Entity exists with right shape (`CatalogEntities.cs:104-116`), DbSet (`AadhiDbContext.cs:40`) — but **zero OnModelCreating config, zero reads/writes anywhere**. Live model = single `Product.CategoryId` FK (`CatalogEntities.cs:67`; `AadhiDbContext.cs:76-79`). No exactly-one-primary rule.

### 2.3 ProductVariant — MISSING (dead entity)
- Entity exists (`CatalogEntities.cs:118-133`) with bare `StockQuantity` int; **no Barcode**, no `StockItem.VariantId` (`InventoryEntities.cs:23-40`), no endpoints, no config, no logic.

### 2.4 ProductImage / upload — PARTIAL
- Entity correct (`CatalogEntities.cs:45-58`). **No upload endpoint** — no `IFormFile` anywhere; products accept only `List<string> ImageUrls` (`CatalogContracts.cs:151`). `LocalFileStorageService` registered but zero call sites. Fabricated `Rating = 4.8, ReviewCount = 86` on every product (`CatalogService.cs:689-690`). `ProductReview` entity exists (`CatalogEntities.cs:149-164`) — unused.

### 2.5 Uniqueness — PARTIAL
- Unique indexes ✔ (`AadhiDbContext.cs:63-64,103,119`). SKU dup-check create-only (`CatalogService.cs:419-422`); **no slug collision check** (create `:416/:428`, update `:525`) → duplicate name = raw 500. Unique index not filtered by `IsDeleted` → soft-deleted SKU reuse passes app check then 500s. `UpdateProductAsync` silently ignores SKU changes (`:524-546`).

### 2.6 Category — PARTIAL
- Hierarchy ✔ (`CatalogEntities.cs:12-14`; Restrict FK `AadhiDbContext.cs:107-110`; recursive tree `CatalogService.cs:71-100`).
- **Circular-parent guard MISSING** (`CatalogService.cs:167` — A→B→A accepted; both then vanish from the root tree).
- **Delete-with-active-products guard MISSING** (`:200-219` — soft-delete leaves products pointing at deleted category → "Uncategorized" fallback `:670`; subcategories dangle).
- Reorder: persists via full PUT only; no bulk-reorder endpoint. Activation audited only via generic update audit.

### 2.7 Bundles/BOM — MISSING
- `GiftBoxItem` (ParentProductId, ComponentProductId, Quantity — `CatalogEntities.cs:135-147`) is dead scaffolding: no config, no service logic, **no component expansion at order time, no component-level stock reservation** (`OrderService.cs:117-129` reserves parent only).

---

## 3. PAYMENTS / REFUNDS / RETURNS

### 3.1 Payment entity — PARTIAL/MISSING pieces
- Fields (`FinanceEntities.cs:33-52`): PaymentNumber, Amount, Method, Status, TransactionReference, PaidAtUtc. **No UTR** (UTR is on Order `OrderEntities.cs:72`), **no IdempotencyKey**, no provider ref. Only unique index = PaymentNumber (`AadhiDbContext.cs:257`) — **same UTR usable across multiple orders**.
- **Two conflicting PAY number generators**: `OrderService.cs:563` (`Random.Shared.Next(100000,999999)`) and `FinanceService.cs:115-116` (`Count()+1`) — collisions → 500 on the unique index.

### 3.2 VerifyPaymentAsync — PARTIAL
- **Not state-guarded**: `order.PaymentStatus = Paid` unconditionally (`OrderService.cs:541`) — works on Cancelled orders. Duplicate Payment rows prevented (`:557`) but verified-metadata overwritten on repeat calls (`:542-544`). PaymentMethod correctly uses `order.PaymentMethod` (`:565`). No outbox event.

### 3.3 RejectPaymentAsync — CORRECT stock, MISSING audit
- Releases BOTH Product (`:654`) and StockItem (`:663`) reservations; double-reject → 409 via state machine. **No audit, no outbox at all** in this flow.

### 3.4 Partial payment — INCORRECT
- `FinanceService.cs:136` sets `order.PaymentStatus = Paid` **for any amount** (₹1 against ₹10,000). Invoice logic below correctly computes PartiallyPaid (`:143`) → Order and Invoice contradict. No amount/order-match validation.

### 3.5 Refund — MISSING
- No `Refund` entity/endpoint/status machine anywhere. `RefundPending/Refunded` enum values never assigned. No refundable-amount guard, no duplicate-refund guard.

### 3.6 Returns — MISSING
- No Return entity, no Requested/Approved/Rejected/Received/Inspected/Refunded/Closed statuses, no inspection flow, no endpoints. Only behavior: the auto-restock on `Returned` status (§1.2).

---

## 4. PURCHASING

### 4.1 PO workflow — MISSING approval
- Statuses (`DomainEnums.cs:71-79`): Draft, Submitted, Approved, PartiallyReceived, Received, Cancelled — **no PendingApproval**. POs **born `Submitted`** (`PurchaseService.cs:158`). **No approve/cancel/edit endpoints** (controller has only GET/POST suppliers, GET/POST POs, GET by id, POST goods-receipts — `ErpAndOperationsControllers.cs:219-276`). Draft/Approved/Cancelled unreachable; GRN accepted against never-approved POs (`:298-299`).

### 4.2 GRN — INCORRECT
- Lines carry **only `QuantityReceived`** (`PurchaseEntities.cs:79`; `InventoryContracts.cs:178-184`). **No Ordered/Rejected/Damaged/Accepted split.**
- **Total delivered → sellable stock** (`PurchaseService.cs:258-278`: `product.StockQuantity += itemReq.QuantityReceived` and StockItem likewise).
- **No over-receive guard** (`:256` unconditional) — receive 1,000 against 10 ordered; PO becomes `Received`.
- **No product↔PO-line match** (`:236-240` — poItem by id, product by independent ProductId): receipt against PO line X can credit stock and ledger to unrelated product Y.

### 4.3 Supplier bills / payables — MISSING
- Grep for `SupplierBill|BillNumber|Payable|SupplierPayment`: **zero matches** in src. Payment is customer-side only; Invoice is sales-side only. No due dates, no payables computation.

---

## 5. FINANCE / REPORTS

### 5.1 COGS — INCORRECT
- `FinanceService.cs:279`: `Sum(i => i.Quantity * (i.UnitPrice.ToDecimal() * 0.65m)) // Approximate 65% cost price`. `OrderItem` has price snapshots (`OrderEntities.cs:31-39`) but **no `CostPriceAtSale`**.

### 5.2 P&L — INCORRECT
- Revenue = Σ `GrandTotal` for `OrderStatus != Cancelled` (`FinanceService.cs:259,278`) → **includes unpaid orders, tax, shipping** (`GrandTotal` composition: `OrderService.cs:194`) **and Returned orders**. No Net Sales line; returns never deducted.

### 5.3 Fabricated data — INCORRECT (~25 sites, exhaustive)
`ReportService.cs`: `:53` profit = sales × 0.26; `:57` `TotalSales = totalSales > 0 ? totalSales : 2485650m`; `:58` `"+18.5%"`; `:59` orders fallback 1248; `:60` `"+12.4%"`; `:61` customers fallback 856; `:62` `"+8.7%"`; `:63` profit fallback 645230; `:64` `"+22.1%"`; `:65` low-stock fallback 23; `:66` pending fallback 17; `:67` stock value fallback 12,540,000; `:68` outstanding fallback 875,230; `:69` invoices fallback 23; `:70` today sales fallback 98,450; `:71` today orders fallback 42; `:72-73` "AADHI CRACKERS" 65% brand share; `:99-111` fake May sales curve when empty; `:131` `TotalDiscount = 24800m` always; `:132` tax = trend × 0.18 fabricated; `:139-147` **top-categories endpoint 100% hardcoded, never queries DB**; `:161-168` fallback top products; `:171-185` real products get invented units `350 - (idx * 50)`; `:190-196` **payment-methods endpoint 100% hardcoded, never queries DB**.
Also: `FinanceService.cs:279` (COGS ratio); `FinanceContracts.cs:129-144` fabricated DTO defaults; `CatalogService.cs:689-690` ratings; `InfrastructureServices.cs:163-164` search ratings.

### 5.4 Dashboard vs report reconciliation — INCORRECT
- Dashboard KPIs: **all-time**, no date parameter (`ErpAndOperationsControllers.cs:451-458`; `ReportService.cs:31-40`). Sales overview: windowed (`:80-87`) + fabricated fallbacks. Same base measure, different windows + fallbacks → can never reconcile.

### 5.5 Expense workflow — MISSING
- `Expense` (`FinanceEntities.cs:54-69`) has **no status field**; posts directly (`FinanceService.cs:209-252`). No Draft/Submitted/Approved/Posted, no approval audit.

### 5.6 Receivables/Payables — PARTIAL / MISSING
- No dedicated endpoints. Dashboard `OutstandingAmount` computed from real invoice balances (`ReportService.cs:49-50`) but poisoned by the 875,230 fallback. Payables impossible (no bills).

### 5.7 Settings — INCORRECT (never read)
- Seeded: `Tax.GstRate=18.00`, `Shipping.FreeShippingThreshold=3000`, `Shipping.StandardCharge=150` (`DatabaseSeeder.cs:804-806`). `GetSettingValueAsync` has **zero business call sites**. Shipping hardcoded twice: `OrderService.cs:190-192` and `OrderContracts.cs:26`. `ReportService.cs:132` hardcodes 0.18. `PUT /settings/{key}` changes nothing at runtime.

---

## 6. PROMOTIONS

- **No status lifecycle** (only `IsActive` + dates — `Promotion.cs:16-21`); Draft/Scheduled/Expired/Disabled don't exist. **No CRUD/admin endpoints** — the 3 seeded coupons are the only promotions possible.
- **PerCustomerLimit declared, never enforced** (`Promotion.cs:20`; not referenced in `IsValidForOrder` `:28-39` or anywhere; no redemption record entity).
- **UsedCount increment racy** (`OrderService.cs:181-186` read-modify-write; no concurrency token on Promotion).
- **No rollback on cancel/reject** — cancellation paths never decrement `UsedCount`.
- Coupon math is server-side via `POST /cart/calculate` (`CatalogAndCartControllers.cs:229-235`; `SettingsAndCartService.cs:142-151`) ✔ — but the customer UI never calls it (§9), and **cart totals exclude tax** (`OrderContracts.cs:26-27`) while orders add it (`OrderService.cs:133,194`) → quoted total ≠ charged total.

---

## 7. INVOICES & NUMBERING

### 7.1 Invoice snapshot — PARTIAL
- Snapshots monetary totals only (`FinanceEntities.cs:15-21`). **No line items, no customer/address snapshots** — renders live `Customer` (`FinanceService.cs:62`): renames rewrite history's display. No credit-note/cancel path — cancelled orders keep `Issued` invoices with open balances forever (Cancelled branch `OrderService.cs:269-305` never touches invoices).

### 7.2 Numbering — INCORRECT (all series)
- ORD `OrderService.cs:750-754`, INV `:756-760`, PO `PurchaseService.cs:150-151`, GRN `:221-222`, PAY `FinanceService.cs:115-116`, EXP `:211-212`, SUP `PurchaseService.cs:63` — **all `CountAsync()+1`**: (a) concurrent duplicate → unique-index 500; (b) soft-delete shrinks the filtered count → next number collides with a live row; (c) no year reset. Second PAY generator uses `Random` (`OrderService.cs:563`). Also random `TrackingNumber` (`:99`) and `CustomerCode` (`:60`) with unique index.

---

## 8. PLATFORM

### 8.1 Audit — PARTIAL
- `LogAsync` persists via its own `SaveChangesAsync` **inside `catch { }`** — failures silently swallowed, not even logged (`AuditLogService.cs:93,114-121`). Audit is a second transaction, not atomic with the business write.
- **Unaudited flows:** `MoveToPackingAsync` (`OrderService.cs:603-627`), `RejectPaymentAsync` (`:629-684`).
- Masking PARTIAL: regex only matches flat string values (`AuditLogService.cs:241-255`); compiled `SensitiveKeyRegex` (`:39-41`) is dead code; UTR written unmasked (`OrderService.cs:596`).
- Append-only by omission only — no trigger/interceptor; DbSet fully mutable.
- `ChangedFieldsJson` **never supplied by any caller** — always null. Full EF entities serialized into audit JSON (cycle-safe via `IgnoreCycles` `:232-236`, but leaks internals like CostPrice).

### 8.2 Outbox — INCORRECT (core broken)
- `EnqueueAsync` only `Add()`s — comment says outer unit of work will save (`InfrastructureServices.cs:21-34`).
- **Order create:** save `:228` → audit `:231` → `EnqueueAsync("OrderPlaced")` **`:241` — after the last save → NEVER PERSISTED.** Same for `OrderStatusChanged` `:410`.
- Worker (`BackgroundServices.cs:24-122`): real handler structure for OrderPlaced/OrderStatusChanged (which never arrive), **default case marks unknown types Processed after a LogDebug** (`:92-94, :99`) — so `AuditLogCreated`, the only type ever persisted, is a no-op. Retry = counter only; no `Status`/`NextAttemptAt` columns, **no backoff, no dead-letter** (RetryCount≥5 filtered forever, `:37`). NotificationService handlers end in `LogInformation` — no real work (`InfrastructureServices.cs:85-104`).

### 8.3 Transactions — MISSING
- Zero `BeginTransaction`/`TransactionScope` in src. `IApplicationDbContext` exposes only `SaveChangesAsync` (`IApplicationInterfaces.cs:41`) — services **cannot** open transactions. Multi-save flows (order create has 2-3 saves) are not atomic.

### 8.4 Authorization — PARTIAL roles, MISSING permissions, MISSING IDOR
- Role policies cover most admin writes ✔ (full endpoint map inspected). Critical holes:
  - `GET /orders`, `/orders/{id}`, `/orders/customer/{customerId}` are **plain `[Authorize]`** (`ErpAndOperationsControllers.cs:32,46,57`) — **any authenticated Customer reads every order** (names, phones, addresses, payment proofs, UTR). No ownership checks anywhere (`OrderService.cs:458-481`).
  - Anonymous `/orders/track/{q}` **matches by phone number** (`OrderService.cs:491`) → anonymous PII/order enumeration.
  - **Permissions decorative:** claims in JWT (`IdentityService.cs:328-331`) and UserDto, but zero `RequireClaim`/handlers/policy-provider (grep). Not stored in DB — only a code switch (`IdentityService.cs:347-391`).
  - `UpdateUserRoleAsync` validRoles omits Manager/PurchaseManager/SupportAgent (`IdentityService.cs:244`) — SuperAdmin cannot assign 3 of the 8 roles.
- JWT: secret committed to `appsettings.json:18`; **two different hardcoded fallbacks** (`IdentityService.cs:311` vs `DependencyInjection.cs:57-60` — sign/validate would mismatch); 24 h tokens; **no refresh mechanism**; dead `AadhiAuth` cookie nothing reads.
- Login history persisted ✔ (`IdentityService.cs:52-64`) but IP/UserAgent never populated. **Rate-limit rejections never persisted or logged** (`RateLimitingPolicies.cs:27-49`; zero `RateLimitLogs.Add`) → `/auth/rate-limit-logs` permanently empty.

### 8.5 Rate limiting — PARTIAL
- 10 policies, all hardcoded (no IConfiguration; `appsettings.json` RateLimiting section unread). Login comment says 5/min, code says 10 (`:63,69`). `Retry-After` statically "60" even for 15-min window (`:34`). 3 policies unused (PasswordReset, ProductSearch, Checkout). No global limiter — `/products/id/{id}`, `/orders/track`, `/search`, all `/finance/*` unlimited.

### 8.6 Validation — MISSING execution
- `AddValidatorsFromAssembly` only (`Application\DependencyInjection.cs:11`). **Zero `ValidateAsync`/`IValidator` usage**; no auto-validation package; MediatR referenced, never registered. All 8 validators (`ApplicationValidators.cs:10-110`) dead. ProblemDetails `ValidationException` branch can never fire.

### 8.7 Search / Elasticsearch — MISSING
- No ES client/package/index code anywhere; `appsettings` "Elasticsearch" section unread. `SearchService` = EF LIKE query with fabricated `Rating = 4.8, ReviewCount = 86` (`InfrastructureServices.cs:107-170`). No `/search/global` endpoint (only `/search?q=`, anonymous, unthrottled).

### 8.8 Data layer — INCORRECT/PARTIAL
- **No migrations**: `EnsureCreatedAsync` only (`DatabaseSeeder.cs:21`); no Migrations folder despite `MigrationsAssembly` configured — schema cannot evolve without dropping the DB.
- **No concurrency tokens anywhere** (no `IsRowVersion`/`IsConcurrencyToken`/`[Timestamp]`); `ConcurrencyConflictException` never thrown.
- Zero fluent config for: Warehouse, GoodsReceipt(Item), OrderStatusHistory, ProductCategory, ProductVariant, GiftBoxItem, ProductReview, LoginHistory, RateLimitLog (no indexes on LoginHistory/RateLimitLog timestamps).
- Soft-delete filters on 12 entities; none on OrderItem, StockItem, StockMovement, AuditLog, OutboxMessage, etc.

### 8.9 Health/observability — PARTIAL/MISSING
- One health check (DbContext). **`/health/live` evaluates zero checks → always Healthy.** No outbox-backlog/worker/storage checks. No OpenTelemetry/metrics. **No `/system-health` endpoint exists** (admin UI calls it → 404).

---

## 9. ADMIN UI (wiring reality)

### 9.1 Broken write-paths (look wired, are broken — verified both sides)
1. **Stock adjust DESTRUCTIVE**: UI sends `adjustmentQuantity` (`inventoryApi.ts:66-73`); backend `StockAdjustmentRequest{adjustedQuantity, isRelative}` (`InventoryContracts.cs:53-60`) binds `{0, false}` = **"set on-hand to 0"** — an admin adjustment zeroes inventory.
2. **All order status buttons broken**: UI body `{status,notes,trackingNumber,carrier}` (`orderApi.ts:44-49`) vs backend `{newStatus, reason}` (`OrderContracts.cs:142-146`) — `newStatus` binds 0 (invalid; Pending=1). moveToPacking/rejectPayment share the bug.
3. **Payment verify 404**: UI posts `/orders/{id}/verify-upi` (`orderApi.ts:62`); backend route `/verify-payment` (`ErpAndOperationsControllers.cs:87`).
4. **PO quantity 0**: UI item field `quantityOrdered` (`ErpPurchaseOrdersModule.tsx:139`) vs backend `Quantity` (`InventoryContracts.cs:137-142`).
5. **`GET /customers` doesn't exist in backend** → 404 cascades: Sales & Orders module `Promise.all` rejects → all tabs blank; global search dies.
6. Sales chart reads `salesByDate`; backend sends `salesTrend` (`FinanceContracts.cs:83-91`) → chart data always empty → dashboard renders hardcoded demo series (`ErpDashboardPage.tsx:261-267`). P&L adapter maps wrong field names (`api.ts:158-180`) → statement rows ₹0.
7. Reports export: wrong URL (`export-${type}` vs `/export/{type}`), hardcoded `http://localhost:5050`, wrong token key (`aadhi_admin_token` vs real `aadhi_admin_jwt_token`), anchor can't carry Bearer → always fails; correct `reportApi.exportCsv` is dead code (`ErpReportsCenterModule.tsx:40-41`).
8. Login-history & rate-limit tabs: backend returns plain lists; frontend paged-wraps (`wrapPagedResult` reads `.items`) → **permanently empty**. `updateUserRole` sends object vs `[FromBody] string` → 400. Audit date filters `fromDate` vs `fromDateUtc` → ignored; diff drawer reads detail-only JSON fields off list rows, detail endpoint never called → always "—". `PagedResult.pageNumber` vs frontend `data?.page` → page metadata always 1.

### 9.2 Hard stubs with success toasts (no persistence)
`getQuotes`, `convertQuoteToOrder` (fake ORD-number), `getReturns`, `updateReturnStatus`, `getStockTransfers`, `getGoodsReceivedNotes`, `getSupplierBills`, `getReceivables`, `getPayables`, `getCoupons`, `createCoupon` (fake CPN-number), `deleteCoupon`, `getProductReviews`, `updateReviewStatus`, `getHomepageBanners` (`api.ts:98-187`) — plus toast-only buttons: GRN "Receive Goods", low-stock "Quick PO", "Send Reminder", "Reset Password", "Create Hot Backup Now". Marketing/coupons module is **entirely** fake-success. `getSystemHealth` calls a nonexistent endpoint → Settings/Health tabs break together.

### 9.3 Table/form standard — PARTIAL
- **No page passes pagination params; zero pagination controls** (grep `setPage|totalPages` in pages: 0 hits) — every table permanently page 1 (~20 rows).
- No `min` attributes on any number input (negative prices/quantities accepted). Only 3 of ~12 submit handlers disable during save. `ErpEmptyState`/`ErpErrorState` exist but unused by module pages. Client filtering runs over page 1 only.
- Route guard checks roles only; **no route passes `requiredRoles`; `user.permissions` never read** — every role sees every module, then hits unhandled 403s (only 401 intercepted). LoginPage ships prefilled SuperAdmin demo credentials.
- 30 `any` usages incl. load-bearing ones (adjustStock params, dashboard state, createPurchaseOrder payload).
- Six orphaned legacy pages (ErpOrdersPage, ErpProductsPage, ErpInventoryPage, ErpSettingsPage, ErpAuditLogsPage, ErpPurchasesAndFinancePages, root LoginPage) imported nowhere — dead code.

---

## 10. CUSTOMER UI

**Fixed since last audit:** demo cart + auto-DIWALI2026 removed; `lineTotal` clamped; mobile product detail fetches real data; TrackOrder field names dual-read.

**Still broken:**
- Hardcoded prefilled shipping addresses, desktop and mobile ("Arun Kumar, 123 West…" — `CheckoutPage.tsx:37-46`, `Screen5CheckoutAndOrderFlow.tsx:39-47`); desktop step 1 has no validation.
- Mobile confirmation shows fake `ORD-2026-000124` — Screen5 passes the real number but `App.tsx:150-152` drops the params; desktop `order-placed` route hardcodes `AADHI123456` (`App.tsx:270`); mobile tracking timeline fully hardcoded (`Screen5:517-528`).
- **Order history calls admin-scoped `GET /orders`** (`AccountPage.tsx:35`) → 401 → swallowed → "No orders placed yet"; `getCustomerOrders` exists, never called. Customer auth entirely fake (never calls API — `AuthContext.tsx:12-53`).
- **Pricing 100% client-computed** (`CartContext.tsx:106-119`) incl. the UPI QR amount; `POST /cart/calculate` never called; coupons are a hardcoded client list (`:93-100`); **mobile checkout doesn't send `couponCode`** (`Screen5:88-119`) → displayed vs charged totals diverge.
- Hardcoded `http://localhost:5050/api/v1` base URL (`services/api.ts:11`); static per-session correlation ID (`:19`).
- Shop filters send `category`/`brand` names; backend binds `categorySlug`/`brandId` → server filtering no-op; client filters page 1 (20 items) only (`ShopPage.tsx:57-65`).
- Quantity steppers still use `availableQuantity || 99` (`ProductDetailPage.tsx:251`, `Screen3:164`) — mitigated by cart clamp only.

---

## 11. CRITICAL-TEST FORECAST (Phase 56 scenarios, as code stands)

| # | Test | Predicted result |
|---|---|---|
| 1 | Concurrent last-unit orders → one 409 | **FAIL** — both succeed (no transaction/concurrency token) |
| 2 | Cancel releases reservation only | **PASS** |
| 3 | Ship: deduct once, exactly one Sale movement | **FAIL** — deducts once ✔ but writes two Sale movements |
| 4 | No auto-restock on return | **FAIL** — restocks instantly, no inspection |
| 5/6 | Sellable/damaged inspection movements | **FAIL** — flow doesn't exist |
| 7 | Duplicate payment submission → one logical payment | **FAIL** — no idempotency/UTR uniqueness |
| 8 | Duplicate refund rejected | **FAIL** — refunds don't exist |
| 9 | ES down → order continues, outbox pending | N/A — no ES; order events lost regardless |
| 10 | Handler failure → retry, NOT Processed | **FAIL** — unknown types marked Processed; no backoff/dead-letter |
| 11 | Dashboard = Sales report | **FAIL** — different windows + fabricated fallbacks |
| 12 | No-permission call → 403 | **FAIL** — permissions unchecked; IDOR open |

---

## 12. DEPENDENCY-ORDERED FIX SEQUENCE (proposed — NOT implemented)

1. **Foundations:** EF migrations; concurrency tokens (Product/StockItem/Promotion); transaction capability on `IApplicationDbContext`; atomic ordering (business + audit + outbox in ONE transaction); numbering via sequence table/retry.
2. **Stock truth:** StockItem sole authority (Product columns become projections/read models); add `StockReserved`/`StockReservationReleased` movement types; single Sale movement at shipment; consistent Before/Change/After frames.
3. **Contract repairs (unblocks admin UI):** fix the 8 mismatches in §9.1; add missing `/customers` and `/system-health` endpoints; PagedResult field alignment.
4. **Security:** permission-based policies enforced server-side; ownership checks on order/customer reads; remove phone-number match from anonymous tracking; secret out of source; execute FluentValidation.
5. **Missing modules:** Returns (entity + workflow + inspection) → Refund entity (idempotent) → PO approval workflow → GRN accepted/rejected/damaged → SupplierBill + payables.
6. **Finance truth:** `CostPriceAtSale` on OrderItem; real P&L with Net Sales; delete all ~25 fabricated sites; settings-driven shipping/GST; reconciling dashboard/report queries with shared date-range parameters.
7. **Outbox correctness:** persist events, Status/NextAttemptAt, backoff, dead-letter, real handlers; then ES behind the outbox (optional).
8. **UI standard:** real pagination everywhere; delete all stub adapter functions or wire them; permission-gated actions; form validation + submit-disable; customer checkout via `/cart/calculate`; env-driven base URL.
9. **Tests:** implement the 12 Phase-56 scenarios as automated tests; then E2E flows (Phases 57-59) with recorded evidence → `docs/final-business-flow-result.md`.

---

*End of Phase 0 report. No code, configuration, or data was modified during this inspection.*
