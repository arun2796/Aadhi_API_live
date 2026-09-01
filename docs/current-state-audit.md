# AADHI CRACKERS — ENTERPRISE REPOSITORY AUDIT

**Date:** 2026-09-01  
**Auditor:** Principal Enterprise Software Architect  
**Repositories:**
- **Backend:** `Aadhi_API_live` (ASP.NET Core 10 LTS Web API — Clean Architecture)
- **Frontend:** `Aadhi_UI_live` (React 19 + TypeScript + Vite + Tailwind CSS)

---

## 1. Existing Architecture

### Backend (`Aadhi_API_live`)
- **Pattern:** 5-layer Clean Architecture
  - `AadhiCrackers.Domain`: Pure domain entities, value objects (`Money`), enums, exceptions.
  - `AadhiCrackers.Application`: Interfaces (`IApplicationDbContext`, `IIdentityService`), business services (`CatalogService`, `OrderService`, `InventoryService`, `FinanceService`, `PurchaseService`, `ReportService`, `AuditLogService`).
  - `AadhiCrackers.Infrastructure`: EF Core `AadhiDbContext` with SQLite WAL mode, ASP.NET Identity, JWT token generator, Serilog structured logger, outbox background processor.
  - `AadhiCrackers.Api`: ASP.NET Core Web API controllers, ProblemDetails middleware, Correlation ID middleware, ASP.NET Core rate limiting.
  - `AadhiCrackers.Contracts`: DTOs, request/response models.
- **Database:** SQLite with Write-Ahead Logging (`WAL`), decimal money converter storing minor currency units.

### Frontend (`Aadhi_UI_live`)
- **Admin App (`admin-web` on port 5174):**
  - React 19 + TypeScript + Vite.
  - Tailwind CSS with Enterprise ERP Dark Navy theme (`#111238`), purple accents (`#4F2ACB`), orange CTAs (`#FF7A00`), gold badges (`#FFB000`).
  - React Router DOM configured with §102 route map, lazy loading, and route guards.
  - Axios HTTP client with modular API services (`apiClient.ts`, `authApi.ts`, `productApi.ts`, etc.).
  - Recharts for visual trends, donut category breakdowns, and revenue graphs.
- **Customer Storefront (`customer-web` on port 5173):**
  - Decoupled customer e-commerce interface, responsive mobile-first design, UPI QR code display & payment screenshot verification upload step.

---

## 2. Existing Modules

1. **Dashboard & KPIs:** Today's revenue, order counts, pending review queue, low-stock alerts, monthly trends.
2. **Sales & Orders:** Orders management, customer CRM with LTV, B2B wholesale quotes, tax invoices, payment records, returns ledger.
3. **Catalog & Products:** Category management, product catalog, variant matrix, combos & gift boxes (BOM), product reviews moderation, homepage banners.
4. **Inventory & Warehouses:** Multi-warehouse stock ledger, physical stock adjustments, inter-warehouse transfers, stock movement audit trail, low-stock alerts.
5. **Purchases & Suppliers:** Supplier directory, purchase orders (PO), goods receiving inspection (GRN), supplier bills.
6. **Finance & P&L:** Categorized operating expenses, real-time Profit & Loss statement, Accounts Receivable & Payable aging.
7. **Marketing & Coupons:** Flat and percentage discount coupons with minimum thresholds and maximum caps.
8. **Reports Center:** Visual reporting and server-side CSV exports for Sales, Products, Inventory, and GST.
9. **Security & Audit:** Interactive JSON Diff inspector (`beforeJson` vs `afterJson`), user role RBAC, login history, rate-limit security logs.
10. **Settings & Health Diagnostics:** Store metadata, UPI payment configuration, live subsystem probes (API, DB, Outbox, Worker, RateLimiter).

---

## 3. Existing Database Entities

| Entity | Table Name | Status | Responsibility |
|---|---|---|---|
| `Product` | `Products` | **IMPLEMENTED** | Catalog item master with SKU, slug, pricing, safety info. |
| `Category` | `Categories` | **IMPLEMENTED** | Hierarchical categories with slug, display order, SEO metadata. |
| `ProductCategory` | `ProductCategories` | **IMPLEMENTED** | Multi-category junction with `IsPrimary` flag. |
| `ProductVariant` | `ProductVariants` | **IMPLEMENTED** | Multi-size/shot variant packaging with SKU, price, weight. |
| `GiftBoxItem` | `GiftBoxItems` | **IMPLEMENTED** | Bill-of-Materials (BOM) bundle item components. |
| `ProductReview` | `ProductReviews` | **IMPLEMENTED** | Customer ratings, reviews, moderation status. |
| `Brand` | `Brands` | **IMPLEMENTED** | Manufacturer brand master. |
| `Warehouse` | `Warehouses` | **IMPLEMENTED** | Physical warehouse storage locations. |
| `StockItem` | `StockItems` | **IMPLEMENTED** | Authoritative warehouse-specific inventory state. |
| `StockMovement` | `StockMovements` | **IMPLEMENTED** | Immutable inventory audit movement ledger. |
| `Order` | `Orders` | **IMPLEMENTED** | Authoritative order record, status history, shipping details. |
| `OrderItem` | `OrderItems` | **IMPLEMENTED** | Line items with historical price and cost price snapshots. |
| `OrderStatusHistory` | `OrderStatusHistories` | **IMPLEMENTED** | State transition audit trail. |
| `Customer` | `Customers` | **IMPLEMENTED** | Customer profile, addresses, lifetime value. |
| `Payment` | `Payments` | **IMPLEMENTED** | Transactional payment records, UTR, screenshots. |
| `Invoice` | `Invoices` | **IMPLEMENTED** | Tax invoices with historical amounts and GST. |
| `Supplier` | `Suppliers` | **IMPLEMENTED** | Raw material / fireworks vendors and payment terms. |
| `PurchaseOrder` | `PurchaseOrders` | **IMPLEMENTED** | PO records and line items. |
| `GoodsReceipt` | `GoodsReceipts` | **IMPLEMENTED** | GRN records with inspected vs rejected quantities. |
| `SupplierBill` | `SupplierBills` | **IMPLEMENTED** | Vendor bills, due dates, balance tracking. |
| `Expense` | `Expenses` | **IMPLEMENTED** | Categorized operating expenses. |
| `Coupon` | `Coupons` | **IMPLEMENTED** | Promotional discount codes. |
| `AuditLog` | `AuditLogs` | **IMPLEMENTED** | Append-only enterprise audit records with before/after diffs. |
| `LoginHistory` | `LoginHistories` | **IMPLEMENTED** | User session login attempt tracking. |
| `RateLimitLog` | `RateLimitLogs` | **IMPLEMENTED** | Rate-limit security violation tracking. |
| `OutboxMessage` | `OutboxMessages` | **IMPLEMENTED** | Transactional outbox pattern queue. |

---

## 4. Existing API Endpoints

### Authentication & Security (`/api/v1/auth`)
- `POST /login` (Rate-limited: 5/min)
- `GET /me` (Authenticated)
- `GET /users` (`RequireAdmin`)
- `PUT /users/{id}/role` (`RequireSuperAdmin`)
- `GET /login-history` (`RequireAdmin`)
- `GET /rate-limit-logs` (`RequireAdmin`)

### Catalog & Products (`/api/v1/products`, `/api/v1/categories`, `/api/v1/brands`)
- `GET /products` (Public, Paged, Filtered)
- `GET /products/{slug}` & `GET /products/id/{id}` (Public)
- `GET /products/featured`, `best-sellers`, `gift-boxes`, `combo-offers` (Public)
- `POST /products`, `PUT /products/{id}`, `DELETE /products/{id}` (`RequireAdmin`)
- `GET /categories`, `GET /categories/{slug}` (Public)
- `POST /categories`, `PUT /categories/{id}`, `DELETE /categories/{id}` (`RequireAdmin`)
- `GET /brands`, `POST /brands` (`RequireAdmin`)

### Inventory & Stock (`/api/v1/inventory`)
- `GET /inventory/warehouses` (`RequireInventoryManager`)
- `GET /inventory/stock` (`RequireInventoryManager`)
- `POST /inventory/adjustments` (`RequireInventoryManager`)
- `POST /inventory/transfers` (`RequireInventoryManager`)
- `GET /inventory/movements` (`RequireInventoryManager`)
- `GET /inventory/low-stock` (`RequireInventoryManager`)

### Orders & Sales (`/api/v1/orders`, `/api/v1/customers`)
- `POST /orders` (Public checkout)
- `GET /orders` (`RequireStaff`)
- `GET /orders/{id}` (`RequireStaff`)
- `PUT /orders/{id}/status` (`RequireStaff` — State machine validated)
- `POST /orders/{id}/verify-upi` (`RequireStaff` — 1-click verify & move to packing)
- `GET /orders/track/{orderNumber}` (Public tracking)
- `GET /customers`, `GET /customers/{id}` (`RequireStaff`)

### Purchases & Procurement (`/api/v1/purchases`)
- `GET /purchases/suppliers`, `POST /purchases/suppliers` (`RequirePurchaseManager`)
- `GET /purchases`, `GET /purchases/{id}`, `POST /purchases` (`RequirePurchaseManager`)
- `POST /purchases/goods-receipts` (`RequirePurchaseManager`)

### Finance & Accounting (`/api/v1/invoices`, `/api/v1/payments`, `/api/v1/finance`, `/api/v1/reports`)
- `GET /invoices` (`RequireAccountant`)
- `GET /payments`, `POST /payments` (`RequireAccountant`)
- `GET /finance/expenses`, `POST /finance/expenses` (`RequireAccountant`)
- `GET /reports/dashboard` (`RequireStaff`)
- `GET /reports/sales-overview`, `top-categories`, `top-products`, `payment-methods` (`RequireStaff`)
- `GET /reports/profit-loss` (`RequireAccountant`)
- `GET /reports/export/{reportType}` (`RequireAdmin` — CSV export)

### Settings & Health (`/api/v1/settings`, `/health`, `/system-health`)
- `GET /settings` (`RequireAdmin`)
- `PUT /settings/{key}` (`RequireSuperAdmin`)
- `GET /health`, `/health/live`, `/health/ready` (Public probes)
- `GET /system-health` (`RequireAdmin`)

---

## 5. Existing UI Pages

### Admin ERP (`Aadhi_UI_live/admin-web`)
- `/login`: Branded login page with JWT auth & quick demo fill.
- `/admin/dashboard`: Real-time KPI cards, sales trend chart, category donut, recent orders, quick actions.
- `/admin/orders`: Order management, UPI payment screenshot review modal, order state transitions.
- `/admin/customers`: Customer directory, lifetime value metrics, order history.
- `/admin/quotes`: B2B quote creation with 1-click conversion to live orders.
- `/admin/invoices`: Tax invoice ledger with GST breakdowns.
- `/admin/payments`: Real-time payment verification ledger.
- `/admin/returns`: Customer returns inspection and refund processing.
- `/admin/products`: Product catalog table with filters, search, modal editor.
- `/admin/categories`: Category management with display orders and active status.
- `/admin/combo-offers`: Bundle & Combo deals with normal value vs offer price savings.
- `/admin/reviews`: Product reviews moderation (Approve, Reject, Hide).
- `/admin/banners`: Promotional homepage banners.
- `/admin/inventory`: Multi-warehouse stock ledger, available stock calculation.
- `/admin/stock-transfers`: Inter-warehouse transfer initiation.
- `/admin/inventory/low-stock`: Critical reorder level alerts.
- `/admin/warehouses`: Warehouse configuration and stock distribution.
- `/admin/purchases`: PO generation, supplier selection, PO statuses.
- `/admin/suppliers`: Vendor management and terms.
- `/admin/goods-received`: GRN delivery inspection.
- `/admin/supplier-bills`: Accounts payable vendor bills.
- `/admin/finance`: Real-time P&L ($Revenue - COGS - OPEX = Net Profit$).
- `/admin/expenses`: Operating expense records.
- `/admin/receivables`: Accounts receivable aging.
- `/admin/payables`: Accounts payable aging.
- `/admin/marketing/coupons`: Coupon discount manager.
- `/admin/reports`: Visual report charts and server-side CSV downloads.
- `/admin/audit-logs`: Enterprise audit log table with interactive JSON diff drawer.
- `/admin/login-history`: User session login security log.
- `/admin/rate-limit-logs`: IP rate-limit violation log.
- `/admin/users`: User management & role assignment.
- `/admin/settings`: Store configuration, UPI gateway settings, order prefixes.
- `/admin/system-health`: Real-time subsystem latency and health probes.
- `/admin/backup`: Hot SQLite database snapshot instructions.

### Customer Web (`Aadhi_UI_live/customer-web`)
- `/`: Responsive mobile-first storefront with category tabs, hero carousel, product catalog, cart drawer, checkout modal, UPI QR code presentation, payment screenshot upload, order confirmation, order tracking.

---

## 6. Existing Business Flows

1. **Customer Order & UPI Flow:**
   - Storefront browsing -> Cart add -> Checkout -> Authoritative backend calculation -> Order created with `Pending` status -> Stock reserved (`QuantityReserved += qty`) -> Customer pays via UPI QR & uploads screenshot -> Admin inspects proof in Orders module -> Admin clicks "Verify & Move to Packing" -> Status changes to `Processing`, Payment marked `Paid` -> Packing -> Handover to courier -> Status updated to `Shipped` (`QuantityOnHand -= qty`, `QuantityReserved -= qty`) -> Out for Delivery -> Delivered.
2. **Procurement & Inventory Flow:**
   - Low stock alert -> Purchase Manager creates PO -> PO approved -> Factory ships crackers -> Goods Receipt Note (GRN) inspection -> Received sellable items added to `StockItem.QuantityOnHand` -> Supplier Bill issued -> Accountant records payment.
3. **P&L Financial Ledger Flow:**
   - Orders generate Net Sales revenue -> Order items capture snapshot COGS -> Operating expenses logged -> Real-time P&L computes Net Operating Profit ($Net Sales - COGS - OPEX$).

---

## 7. Critical Problems & Vulnerabilities (Fixed & Audited)

| Area | Nature of Problem | Severity | Resolution Status |
|---|---|---|---|
| **Inventory Source of Truth** | Independent `Product.StockQuantity` vs `StockItem.QuantityOnHand` competing sources of truth. | **CRITICAL** | **FIXED** — `StockItem` established as sole authoritative source; `Available = QuantityOnHand - Reserved`. |
| **Stock Overselling Concurrency** | Two customers purchasing the final item simultaneously could drive stock negative. | **HIGH** | **FIXED** — Atomic stock validation & reservation in single transaction. |
| **Order State Machine Transitions** | Potential for illegal status transitions (e.g. `Delivered -> Pending`). | **HIGH** | **FIXED** — `Order.ChangeStatus()` validates legal jumps; invalid transitions throw HTTP 409 Conflict. |
| **API Authorization Gaps** | Internal ERP endpoints had `[AllowAnonymous]` without role protection. | **HIGH** | **FIXED** — Registered 8 granular policies; applied `[Authorize(Policy = "...")]` on all ERP routes. |
| **Frontend Fake Data Fallbacks** | Frontend catch blocks returning fake metrics instead of error states. | **MEDIUM** | **FIXED** — Removed all mock fallbacks; created `ErpErrorState` with retry & correlation ID. |
| **Hardcoded API URLs** | Hardcoded `localhost:5050` strings in frontend code. | **MEDIUM** | **FIXED** — Externalized to `VITE_API_BASE_URL` with `.env.example`, `.env.development`, `.env.production`. |

---

## 8. Missing Functionality (Identified & Resolved)

1. **Multi-Category Junction (`ProductCategory`):** Added to allow products to belong to primary and multiple auxiliary categories.
2. **Product Variants (`ProductVariant`):** Added to support 30/60/120 shots packaging.
3. **Gift Box BOM (`GiftBoxItem`):** Added to track component crackers inside celebration boxes.
4. **Login History & Rate Limit Logs:** Added entities, tables, and API query endpoints for security audits.
5. **React Router Deep-Linking:** Configured lazy loading and deep routes for all §102 sub-pages.
6. **Modular API Client:** Refactored monolithic `api.ts` into 15 domain-specific services with error detail extraction and correlation ID capture.

---

## 9. Migration Plan & Next Milestones

1. **Step 1 (Completed):** Foundation correctness, environment variables, error states with correlation IDs, modular API client, React Router setup.
2. **Step 2 (Completed):** Inventory correctness, authoritative `StockItem`, stock reservation lifecycle, immutable movement ledger.
3. **Step 3 (Completed):** Catalog junction and variant models (`ProductCategory`, `ProductVariant`, `GiftBoxItem`).
4. **Step 4 (Completed):** Order state machine and atomic transactions.
5. **Step 5 (Completed):** Role-based authorization policies across all endpoints.
6. **Step 6 (Completed):** 100% build health across backend (`18/18 tests passed`) and frontends (`0 errors`).
7. **Step 7:** Continuous testing against production workloads.
