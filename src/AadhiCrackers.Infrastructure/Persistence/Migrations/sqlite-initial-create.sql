CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory" (
    "MigrationId" TEXT NOT NULL CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY,
    "ProductVersion" TEXT NOT NULL
);

BEGIN TRANSACTION;
CREATE TABLE "AspNetRoles" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_AspNetRoles" PRIMARY KEY,
    "Description" TEXT NULL,
    "Name" TEXT NULL,
    "NormalizedName" TEXT NULL,
    "ConcurrencyStamp" TEXT NULL
);

CREATE TABLE "AspNetUsers" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_AspNetUsers" PRIMARY KEY,
    "FirstName" TEXT NOT NULL,
    "LastName" TEXT NOT NULL,
    "CustomerCode" TEXT NULL,
    "IsActive" INTEGER NOT NULL,
    "CreatedAtUtc" TEXT NOT NULL,
    "LastLoginAtUtc" TEXT NULL,
    "UserName" TEXT NULL,
    "NormalizedUserName" TEXT NULL,
    "Email" TEXT NULL,
    "NormalizedEmail" TEXT NULL,
    "EmailConfirmed" INTEGER NOT NULL,
    "PasswordHash" TEXT NULL,
    "SecurityStamp" TEXT NULL,
    "ConcurrencyStamp" TEXT NULL,
    "PhoneNumber" TEXT NULL,
    "PhoneNumberConfirmed" INTEGER NOT NULL,
    "TwoFactorEnabled" INTEGER NOT NULL,
    "LockoutEnd" TEXT NULL,
    "LockoutEnabled" INTEGER NOT NULL,
    "AccessFailedCount" INTEGER NOT NULL
);

CREATE TABLE "AuditLogs" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_AuditLogs" PRIMARY KEY,
    "TimestampUtc" TEXT NOT NULL,
    "UserId" TEXT NULL,
    "UserName" TEXT NULL,
    "Role" TEXT NULL,
    "Action" INTEGER NOT NULL,
    "Module" TEXT NOT NULL,
    "EntityType" TEXT NOT NULL,
    "EntityId" TEXT NULL,
    "EntityName" TEXT NULL,
    "HttpMethod" TEXT NOT NULL,
    "RequestPath" TEXT NOT NULL,
    "CorrelationId" TEXT NOT NULL,
    "TraceId" TEXT NULL,
    "IpAddress" TEXT NULL,
    "UserAgent" TEXT NULL,
    "ClientApplication" TEXT NULL,
    "Severity" INTEGER NOT NULL,
    "Success" INTEGER NOT NULL,
    "FailureReason" TEXT NULL,
    "BeforeJson" TEXT NULL,
    "AfterJson" TEXT NULL,
    "ChangedFieldsJson" TEXT NULL,
    "MetadataJson" TEXT NULL,
    "CreatedAtUtc" TEXT NOT NULL,
    "UpdatedAtUtc" TEXT NULL,
    "CreatedBy" TEXT NULL,
    "UpdatedBy" TEXT NULL,
    "IsDeleted" INTEGER NOT NULL
);

CREATE TABLE "Brands" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_Brands" PRIMARY KEY,
    "Name" TEXT NOT NULL,
    "Slug" TEXT NOT NULL,
    "Description" TEXT NULL,
    "LogoUrl" TEXT NULL,
    "IsActive" INTEGER NOT NULL,
    "CreatedAtUtc" TEXT NOT NULL,
    "UpdatedAtUtc" TEXT NULL,
    "CreatedBy" TEXT NULL,
    "UpdatedBy" TEXT NULL,
    "IsDeleted" INTEGER NOT NULL
);

CREATE TABLE "Categories" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_Categories" PRIMARY KEY,
    "Name" TEXT NOT NULL,
    "Slug" TEXT NOT NULL,
    "Description" TEXT NULL,
    "ParentCategoryId" TEXT NULL,
    "ImageUrl" TEXT NULL,
    "DisplayOrder" INTEGER NOT NULL,
    "IsActive" INTEGER NOT NULL,
    "SeoTitle" TEXT NULL,
    "SeoDescription" TEXT NULL,
    "CreatedAtUtc" TEXT NOT NULL,
    "UpdatedAtUtc" TEXT NULL,
    "CreatedBy" TEXT NULL,
    "UpdatedBy" TEXT NULL,
    "IsDeleted" INTEGER NOT NULL,
    CONSTRAINT "FK_Categories_Categories_ParentCategoryId" FOREIGN KEY ("ParentCategoryId") REFERENCES "Categories" ("Id") ON DELETE RESTRICT
);

CREATE TABLE "Customers" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_Customers" PRIMARY KEY,
    "UserId" TEXT NOT NULL,
    "CustomerCode" TEXT NOT NULL,
    "FirstName" TEXT NOT NULL,
    "LastName" TEXT NOT NULL,
    "Email" TEXT NOT NULL,
    "Phone" TEXT NOT NULL,
    "DateOfBirth" TEXT NULL,
    "IsActive" INTEGER NOT NULL,
    "CreatedAtUtc" TEXT NOT NULL,
    "UpdatedAtUtc" TEXT NULL,
    "CreatedBy" TEXT NULL,
    "UpdatedBy" TEXT NULL,
    "IsDeleted" INTEGER NOT NULL
);

CREATE TABLE "Expenses" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_Expenses" PRIMARY KEY,
    "ExpenseNumber" TEXT NOT NULL,
    "Category" INTEGER NOT NULL,
    "Description" TEXT NOT NULL,
    "Amount" INTEGER NOT NULL,
    "Tax" INTEGER NOT NULL,
    "PaymentMethod" INTEGER NOT NULL,
    "ExpenseDateUtc" TEXT NOT NULL,
    "Reference" TEXT NULL,
    "CreatedAtUtc" TEXT NOT NULL,
    "UpdatedAtUtc" TEXT NULL,
    "CreatedBy" TEXT NULL,
    "UpdatedBy" TEXT NULL,
    "IsDeleted" INTEGER NOT NULL
);

CREATE TABLE "LoginHistories" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_LoginHistories" PRIMARY KEY,
    "UserId" TEXT NULL,
    "Email" TEXT NOT NULL,
    "TimestampUtc" TEXT NOT NULL,
    "IpAddress" TEXT NULL,
    "UserAgent" TEXT NULL,
    "Success" INTEGER NOT NULL,
    "FailureReason" TEXT NULL,
    "CreatedAtUtc" TEXT NOT NULL,
    "UpdatedAtUtc" TEXT NULL,
    "CreatedBy" TEXT NULL,
    "UpdatedBy" TEXT NULL,
    "IsDeleted" INTEGER NOT NULL
);

CREATE TABLE "OutboxMessages" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_OutboxMessages" PRIMARY KEY,
    "OccurredOnUtc" TEXT NOT NULL,
    "Type" TEXT NOT NULL,
    "PayloadJson" TEXT NOT NULL,
    "ProcessedOnUtc" TEXT NULL,
    "Error" TEXT NULL,
    "RetryCount" INTEGER NOT NULL,
    "CreatedAtUtc" TEXT NOT NULL,
    "UpdatedAtUtc" TEXT NULL,
    "CreatedBy" TEXT NULL,
    "UpdatedBy" TEXT NULL,
    "IsDeleted" INTEGER NOT NULL
);

CREATE TABLE "Promotions" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_Promotions" PRIMARY KEY,
    "Code" TEXT NOT NULL,
    "Name" TEXT NOT NULL,
    "Description" TEXT NULL,
    "DiscountType" INTEGER NOT NULL,
    "DiscountValue" TEXT NOT NULL,
    "MinimumOrderAmount" INTEGER NULL,
    "MaximumDiscount" INTEGER NULL,
    "StartDateUtc" TEXT NULL,
    "EndDateUtc" TEXT NULL,
    "UsageLimit" INTEGER NULL,
    "UsedCount" INTEGER NOT NULL,
    "PerCustomerLimit" INTEGER NULL,
    "IsActive" INTEGER NOT NULL,
    "CreatedAtUtc" TEXT NOT NULL,
    "UpdatedAtUtc" TEXT NULL,
    "CreatedBy" TEXT NULL,
    "UpdatedBy" TEXT NULL,
    "IsDeleted" INTEGER NOT NULL
);

CREATE TABLE "RateLimitLogs" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_RateLimitLogs" PRIMARY KEY,
    "TimestampUtc" TEXT NOT NULL,
    "Endpoint" TEXT NOT NULL,
    "Policy" TEXT NOT NULL,
    "IpAddress" TEXT NULL,
    "RequestsCount" INTEGER NOT NULL,
    "BlockedCount" INTEGER NOT NULL,
    "Reason" TEXT NOT NULL,
    "CreatedAtUtc" TEXT NOT NULL,
    "UpdatedAtUtc" TEXT NULL,
    "CreatedBy" TEXT NULL,
    "UpdatedBy" TEXT NULL,
    "IsDeleted" INTEGER NOT NULL
);

CREATE TABLE "Suppliers" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_Suppliers" PRIMARY KEY,
    "Code" TEXT NOT NULL,
    "Name" TEXT NOT NULL,
    "ContactPerson" TEXT NULL,
    "Email" TEXT NULL,
    "Phone" TEXT NOT NULL,
    "Address" TEXT NULL,
    "GstNumber" TEXT NULL,
    "IsActive" INTEGER NOT NULL,
    "CreatedAtUtc" TEXT NOT NULL,
    "UpdatedAtUtc" TEXT NULL,
    "CreatedBy" TEXT NULL,
    "UpdatedBy" TEXT NULL,
    "IsDeleted" INTEGER NOT NULL
);

CREATE TABLE "SystemSettings" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_SystemSettings" PRIMARY KEY,
    "Key" TEXT NOT NULL,
    "Value" TEXT NOT NULL,
    "Group" TEXT NOT NULL,
    "Description" TEXT NULL,
    "IsEncrypted" INTEGER NOT NULL,
    "CreatedAtUtc" TEXT NOT NULL,
    "UpdatedAtUtc" TEXT NULL,
    "CreatedBy" TEXT NULL,
    "UpdatedBy" TEXT NULL,
    "IsDeleted" INTEGER NOT NULL
);

CREATE TABLE "Warehouses" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_Warehouses" PRIMARY KEY,
    "Code" TEXT NOT NULL,
    "Name" TEXT NOT NULL,
    "Address" TEXT NULL,
    "Phone" TEXT NULL,
    "IsActive" INTEGER NOT NULL,
    "IsPrimary" INTEGER NOT NULL,
    "CreatedAtUtc" TEXT NOT NULL,
    "UpdatedAtUtc" TEXT NULL,
    "CreatedBy" TEXT NULL,
    "UpdatedBy" TEXT NULL,
    "IsDeleted" INTEGER NOT NULL
);

CREATE TABLE "AspNetRoleClaims" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_AspNetRoleClaims" PRIMARY KEY AUTOINCREMENT,
    "RoleId" TEXT NOT NULL,
    "ClaimType" TEXT NULL,
    "ClaimValue" TEXT NULL,
    CONSTRAINT "FK_AspNetRoleClaims_AspNetRoles_RoleId" FOREIGN KEY ("RoleId") REFERENCES "AspNetRoles" ("Id") ON DELETE CASCADE
);

CREATE TABLE "AspNetUserClaims" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_AspNetUserClaims" PRIMARY KEY AUTOINCREMENT,
    "UserId" TEXT NOT NULL,
    "ClaimType" TEXT NULL,
    "ClaimValue" TEXT NULL,
    CONSTRAINT "FK_AspNetUserClaims_AspNetUsers_UserId" FOREIGN KEY ("UserId") REFERENCES "AspNetUsers" ("Id") ON DELETE CASCADE
);

CREATE TABLE "AspNetUserLogins" (
    "LoginProvider" TEXT NOT NULL,
    "ProviderKey" TEXT NOT NULL,
    "ProviderDisplayName" TEXT NULL,
    "UserId" TEXT NOT NULL,
    CONSTRAINT "PK_AspNetUserLogins" PRIMARY KEY ("LoginProvider", "ProviderKey"),
    CONSTRAINT "FK_AspNetUserLogins_AspNetUsers_UserId" FOREIGN KEY ("UserId") REFERENCES "AspNetUsers" ("Id") ON DELETE CASCADE
);

CREATE TABLE "AspNetUserRoles" (
    "UserId" TEXT NOT NULL,
    "RoleId" TEXT NOT NULL,
    CONSTRAINT "PK_AspNetUserRoles" PRIMARY KEY ("UserId", "RoleId"),
    CONSTRAINT "FK_AspNetUserRoles_AspNetRoles_RoleId" FOREIGN KEY ("RoleId") REFERENCES "AspNetRoles" ("Id") ON DELETE CASCADE,
    CONSTRAINT "FK_AspNetUserRoles_AspNetUsers_UserId" FOREIGN KEY ("UserId") REFERENCES "AspNetUsers" ("Id") ON DELETE CASCADE
);

CREATE TABLE "AspNetUserTokens" (
    "UserId" TEXT NOT NULL,
    "LoginProvider" TEXT NOT NULL,
    "Name" TEXT NOT NULL,
    "Value" TEXT NULL,
    CONSTRAINT "PK_AspNetUserTokens" PRIMARY KEY ("UserId", "LoginProvider", "Name"),
    CONSTRAINT "FK_AspNetUserTokens_AspNetUsers_UserId" FOREIGN KEY ("UserId") REFERENCES "AspNetUsers" ("Id") ON DELETE CASCADE
);

CREATE TABLE "Products" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_Products" PRIMARY KEY,
    "SKU" TEXT NOT NULL,
    "Name" TEXT NOT NULL,
    "Slug" TEXT NOT NULL,
    "Description" TEXT NOT NULL,
    "ShortDescription" TEXT NULL,
    "CategoryId" TEXT NOT NULL,
    "BrandId" TEXT NULL,
    "Price" INTEGER NOT NULL,
    "CompareAtPrice" INTEGER NULL,
    "CostPrice" INTEGER NOT NULL,
    "TaxRate" TEXT NOT NULL,
    "DiscountType" INTEGER NOT NULL,
    "DiscountValue" TEXT NOT NULL,
    "StockQuantity" INTEGER NOT NULL,
    "ReservedQuantity" INTEGER NOT NULL,
    "ReorderLevel" INTEGER NOT NULL,
    "MinOrderQuantity" INTEGER NOT NULL,
    "MaxOrderQuantity" INTEGER NOT NULL,
    "Unit" TEXT NOT NULL,
    "WeightKg" TEXT NOT NULL,
    "IsActive" INTEGER NOT NULL,
    "IsFeatured" INTEGER NOT NULL,
    "IsBestSeller" INTEGER NOT NULL,
    "IsNewArrival" INTEGER NOT NULL,
    "SafetyInformation" TEXT NULL,
    "RowVersion" BLOB NULL,
    "CreatedAtUtc" TEXT NOT NULL,
    "UpdatedAtUtc" TEXT NULL,
    "CreatedBy" TEXT NULL,
    "UpdatedBy" TEXT NULL,
    "IsDeleted" INTEGER NOT NULL,
    CONSTRAINT "FK_Products_Brands_BrandId" FOREIGN KEY ("BrandId") REFERENCES "Brands" ("Id") ON DELETE SET NULL,
    CONSTRAINT "FK_Products_Categories_CategoryId" FOREIGN KEY ("CategoryId") REFERENCES "Categories" ("Id") ON DELETE RESTRICT
);

CREATE TABLE "CustomerAddresses" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_CustomerAddresses" PRIMARY KEY,
    "CustomerId" TEXT NOT NULL,
    "AddressType" INTEGER NOT NULL,
    "Address_FullName" TEXT NOT NULL,
    "Address_Phone" TEXT NOT NULL,
    "Address_AddressLine1" TEXT NOT NULL,
    "Address_AddressLine2" TEXT NULL,
    "Address_City" TEXT NOT NULL,
    "Address_State" TEXT NOT NULL,
    "Address_PostalCode" TEXT NOT NULL,
    "Address_Country" TEXT NOT NULL,
    "IsDefault" INTEGER NOT NULL,
    "CreatedAtUtc" TEXT NOT NULL,
    "UpdatedAtUtc" TEXT NULL,
    "CreatedBy" TEXT NULL,
    "UpdatedBy" TEXT NULL,
    "IsDeleted" INTEGER NOT NULL,
    CONSTRAINT "FK_CustomerAddresses_Customers_CustomerId" FOREIGN KEY ("CustomerId") REFERENCES "Customers" ("Id") ON DELETE CASCADE
);

CREATE TABLE "Orders" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_Orders" PRIMARY KEY,
    "OrderNumber" TEXT NOT NULL,
    "CustomerId" TEXT NOT NULL,
    "WarehouseId" TEXT NULL,
    "OrderStatus" INTEGER NOT NULL,
    "PaymentStatus" INTEGER NOT NULL,
    "PaymentMethod" INTEGER NOT NULL,
    "FulfillmentStatus" INTEGER NOT NULL,
    "ItemsSubtotal" INTEGER NOT NULL,
    "Discount" INTEGER NOT NULL,
    "Tax" INTEGER NOT NULL,
    "ShippingCharge" INTEGER NOT NULL,
    "GrandTotal" INTEGER NOT NULL,
    "CouponCode" TEXT NULL,
    "Notes" TEXT NULL,
    "TrackingNumber" TEXT NULL,
    "PlacedAtUtc" TEXT NOT NULL,
    "UtrNumber" TEXT NULL,
    "PaymentScreenshotUrl" TEXT NULL,
    "PaymentSubmittedAtUtc" TEXT NULL,
    "PaymentVerifiedAtUtc" TEXT NULL,
    "PaymentVerifiedBy" TEXT NULL,
    "PaymentVerificationNotes" TEXT NULL,
    "ShippingAddress_FullName" TEXT NOT NULL,
    "ShippingAddress_Phone" TEXT NOT NULL,
    "ShippingAddress_AddressLine1" TEXT NOT NULL,
    "ShippingAddress_AddressLine2" TEXT NULL,
    "ShippingAddress_City" TEXT NOT NULL,
    "ShippingAddress_State" TEXT NOT NULL,
    "ShippingAddress_PostalCode" TEXT NOT NULL,
    "ShippingAddress_Country" TEXT NOT NULL,
    "BillingAddress_FullName" TEXT NULL,
    "BillingAddress_Phone" TEXT NULL,
    "BillingAddress_AddressLine1" TEXT NULL,
    "BillingAddress_AddressLine2" TEXT NULL,
    "BillingAddress_City" TEXT NULL,
    "BillingAddress_State" TEXT NULL,
    "BillingAddress_PostalCode" TEXT NULL,
    "BillingAddress_Country" TEXT NULL,
    "CreatedAtUtc" TEXT NOT NULL,
    "UpdatedAtUtc" TEXT NULL,
    "CreatedBy" TEXT NULL,
    "UpdatedBy" TEXT NULL,
    "IsDeleted" INTEGER NOT NULL,
    CONSTRAINT "FK_Orders_Customers_CustomerId" FOREIGN KEY ("CustomerId") REFERENCES "Customers" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_Orders_Warehouses_WarehouseId" FOREIGN KEY ("WarehouseId") REFERENCES "Warehouses" ("Id")
);

CREATE TABLE "PurchaseOrders" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_PurchaseOrders" PRIMARY KEY,
    "PoNumber" TEXT NOT NULL,
    "SupplierId" TEXT NOT NULL,
    "WarehouseId" TEXT NOT NULL,
    "Status" INTEGER NOT NULL,
    "Subtotal" INTEGER NOT NULL,
    "Tax" INTEGER NOT NULL,
    "GrandTotal" INTEGER NOT NULL,
    "OrderDateUtc" TEXT NOT NULL,
    "ExpectedDeliveryDateUtc" TEXT NULL,
    "Notes" TEXT NULL,
    "CreatedAtUtc" TEXT NOT NULL,
    "UpdatedAtUtc" TEXT NULL,
    "CreatedBy" TEXT NULL,
    "UpdatedBy" TEXT NULL,
    "IsDeleted" INTEGER NOT NULL,
    CONSTRAINT "FK_PurchaseOrders_Suppliers_SupplierId" FOREIGN KEY ("SupplierId") REFERENCES "Suppliers" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_PurchaseOrders_Warehouses_WarehouseId" FOREIGN KEY ("WarehouseId") REFERENCES "Warehouses" ("Id") ON DELETE CASCADE
);

CREATE TABLE "GiftBoxItems" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_GiftBoxItems" PRIMARY KEY,
    "ParentProductId" TEXT NOT NULL,
    "ComponentProductId" TEXT NOT NULL,
    "Quantity" INTEGER NOT NULL,
    "CreatedAtUtc" TEXT NOT NULL,
    "UpdatedAtUtc" TEXT NULL,
    "CreatedBy" TEXT NULL,
    "UpdatedBy" TEXT NULL,
    "IsDeleted" INTEGER NOT NULL,
    CONSTRAINT "FK_GiftBoxItems_Products_ComponentProductId" FOREIGN KEY ("ComponentProductId") REFERENCES "Products" ("Id") ON DELETE CASCADE,
    CONSTRAINT "FK_GiftBoxItems_Products_ParentProductId" FOREIGN KEY ("ParentProductId") REFERENCES "Products" ("Id") ON DELETE CASCADE
);

CREATE TABLE "ProductCategories" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_ProductCategories" PRIMARY KEY,
    "ProductId" TEXT NOT NULL,
    "CategoryId" TEXT NOT NULL,
    "IsPrimary" INTEGER NOT NULL,
    "CreatedAtUtc" TEXT NOT NULL,
    "UpdatedAtUtc" TEXT NULL,
    "CreatedBy" TEXT NULL,
    "UpdatedBy" TEXT NULL,
    "IsDeleted" INTEGER NOT NULL,
    CONSTRAINT "FK_ProductCategories_Categories_CategoryId" FOREIGN KEY ("CategoryId") REFERENCES "Categories" ("Id") ON DELETE CASCADE,
    CONSTRAINT "FK_ProductCategories_Products_ProductId" FOREIGN KEY ("ProductId") REFERENCES "Products" ("Id") ON DELETE CASCADE
);

CREATE TABLE "ProductImages" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_ProductImages" PRIMARY KEY,
    "ProductId" TEXT NOT NULL,
    "Url" TEXT NOT NULL,
    "AltText" TEXT NULL,
    "SortOrder" INTEGER NOT NULL,
    "IsPrimary" INTEGER NOT NULL,
    "CreatedAtUtc" TEXT NOT NULL,
    "UpdatedAtUtc" TEXT NULL,
    "CreatedBy" TEXT NULL,
    "UpdatedBy" TEXT NULL,
    "IsDeleted" INTEGER NOT NULL,
    CONSTRAINT "FK_ProductImages_Products_ProductId" FOREIGN KEY ("ProductId") REFERENCES "Products" ("Id") ON DELETE CASCADE
);

CREATE TABLE "ProductReviews" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_ProductReviews" PRIMARY KEY,
    "ProductId" TEXT NOT NULL,
    "CustomerId" TEXT NULL,
    "CustomerName" TEXT NOT NULL,
    "Rating" INTEGER NOT NULL,
    "Comment" TEXT NOT NULL,
    "Status" TEXT NOT NULL,
    "CreatedAtUtc" TEXT NOT NULL,
    "UpdatedAtUtc" TEXT NULL,
    "CreatedBy" TEXT NULL,
    "UpdatedBy" TEXT NULL,
    "IsDeleted" INTEGER NOT NULL,
    CONSTRAINT "FK_ProductReviews_Products_ProductId" FOREIGN KEY ("ProductId") REFERENCES "Products" ("Id") ON DELETE CASCADE
);

CREATE TABLE "ProductVariants" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_ProductVariants" PRIMARY KEY,
    "ProductId" TEXT NOT NULL,
    "SKU" TEXT NOT NULL,
    "Name" TEXT NOT NULL,
    "Price" INTEGER NOT NULL,
    "CostPrice" INTEGER NOT NULL,
    "StockQuantity" INTEGER NOT NULL,
    "IsActive" INTEGER NOT NULL,
    "CreatedAtUtc" TEXT NOT NULL,
    "UpdatedAtUtc" TEXT NULL,
    "CreatedBy" TEXT NULL,
    "UpdatedBy" TEXT NULL,
    "IsDeleted" INTEGER NOT NULL,
    CONSTRAINT "FK_ProductVariants_Products_ProductId" FOREIGN KEY ("ProductId") REFERENCES "Products" ("Id") ON DELETE CASCADE
);

CREATE TABLE "StockItems" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_StockItems" PRIMARY KEY,
    "ProductId" TEXT NOT NULL,
    "WarehouseId" TEXT NOT NULL,
    "QuantityOnHand" INTEGER NOT NULL,
    "QuantityReserved" INTEGER NOT NULL,
    "ReorderLevel" INTEGER NOT NULL,
    "CreatedAtUtc" TEXT NOT NULL,
    "UpdatedAtUtc" TEXT NULL,
    "CreatedBy" TEXT NULL,
    "UpdatedBy" TEXT NULL,
    "IsDeleted" INTEGER NOT NULL,
    CONSTRAINT "FK_StockItems_Products_ProductId" FOREIGN KEY ("ProductId") REFERENCES "Products" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_StockItems_Warehouses_WarehouseId" FOREIGN KEY ("WarehouseId") REFERENCES "Warehouses" ("Id") ON DELETE RESTRICT
);

CREATE TABLE "StockMovements" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_StockMovements" PRIMARY KEY,
    "ProductId" TEXT NOT NULL,
    "WarehouseId" TEXT NOT NULL,
    "MovementType" INTEGER NOT NULL,
    "QuantityChange" INTEGER NOT NULL,
    "QuantityBefore" INTEGER NOT NULL,
    "QuantityAfter" INTEGER NOT NULL,
    "ReferenceType" TEXT NOT NULL,
    "ReferenceId" TEXT NULL,
    "Reason" TEXT NOT NULL,
    "CreatedAtUtc" TEXT NOT NULL,
    "UpdatedAtUtc" TEXT NULL,
    "CreatedBy" TEXT NULL,
    "UpdatedBy" TEXT NULL,
    "IsDeleted" INTEGER NOT NULL,
    CONSTRAINT "FK_StockMovements_Products_ProductId" FOREIGN KEY ("ProductId") REFERENCES "Products" ("Id") ON DELETE CASCADE,
    CONSTRAINT "FK_StockMovements_Warehouses_WarehouseId" FOREIGN KEY ("WarehouseId") REFERENCES "Warehouses" ("Id") ON DELETE CASCADE
);

CREATE TABLE "Invoices" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_Invoices" PRIMARY KEY,
    "InvoiceNumber" TEXT NOT NULL,
    "OrderId" TEXT NOT NULL,
    "CustomerId" TEXT NOT NULL,
    "Subtotal" INTEGER NOT NULL,
    "Discount" INTEGER NOT NULL,
    "Tax" INTEGER NOT NULL,
    "Shipping" INTEGER NOT NULL,
    "GrandTotal" INTEGER NOT NULL,
    "PaidAmount" INTEGER NOT NULL,
    "BalanceAmount" INTEGER NOT NULL,
    "Status" INTEGER NOT NULL,
    "IssuedAtUtc" TEXT NOT NULL,
    "DueDateUtc" TEXT NOT NULL,
    "CreatedAtUtc" TEXT NOT NULL,
    "UpdatedAtUtc" TEXT NULL,
    "CreatedBy" TEXT NULL,
    "UpdatedBy" TEXT NULL,
    "IsDeleted" INTEGER NOT NULL,
    CONSTRAINT "FK_Invoices_Customers_CustomerId" FOREIGN KEY ("CustomerId") REFERENCES "Customers" ("Id") ON DELETE CASCADE,
    CONSTRAINT "FK_Invoices_Orders_OrderId" FOREIGN KEY ("OrderId") REFERENCES "Orders" ("Id") ON DELETE RESTRICT
);

CREATE TABLE "OrderItems" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_OrderItems" PRIMARY KEY,
    "OrderId" TEXT NOT NULL,
    "ProductId" TEXT NOT NULL,
    "ProductNameSnapshot" TEXT NOT NULL,
    "SKUSnapshot" TEXT NOT NULL,
    "ProductImageUrlSnapshot" TEXT NULL,
    "UnitPrice" INTEGER NOT NULL,
    "Quantity" INTEGER NOT NULL,
    "Discount" INTEGER NOT NULL,
    "Tax" INTEGER NOT NULL,
    "LineTotal" INTEGER NOT NULL,
    "CreatedAtUtc" TEXT NOT NULL,
    "UpdatedAtUtc" TEXT NULL,
    "CreatedBy" TEXT NULL,
    "UpdatedBy" TEXT NULL,
    "IsDeleted" INTEGER NOT NULL,
    CONSTRAINT "FK_OrderItems_Orders_OrderId" FOREIGN KEY ("OrderId") REFERENCES "Orders" ("Id") ON DELETE CASCADE,
    CONSTRAINT "FK_OrderItems_Products_ProductId" FOREIGN KEY ("ProductId") REFERENCES "Products" ("Id") ON DELETE CASCADE
);

CREATE TABLE "OrderStatusHistories" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_OrderStatusHistories" PRIMARY KEY,
    "OrderId" TEXT NOT NULL,
    "FromStatus" INTEGER NOT NULL,
    "ToStatus" INTEGER NOT NULL,
    "Reason" TEXT NULL,
    "ChangedBy" TEXT NULL,
    "ChangedAtUtc" TEXT NOT NULL,
    "CreatedAtUtc" TEXT NOT NULL,
    "UpdatedAtUtc" TEXT NULL,
    "CreatedBy" TEXT NULL,
    "UpdatedBy" TEXT NULL,
    "IsDeleted" INTEGER NOT NULL,
    CONSTRAINT "FK_OrderStatusHistories_Orders_OrderId" FOREIGN KEY ("OrderId") REFERENCES "Orders" ("Id") ON DELETE CASCADE
);

CREATE TABLE "Payments" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_Payments" PRIMARY KEY,
    "PaymentNumber" TEXT NOT NULL,
    "OrderId" TEXT NULL,
    "CustomerId" TEXT NOT NULL,
    "Amount" INTEGER NOT NULL,
    "PaymentMethod" INTEGER NOT NULL,
    "PaymentStatus" INTEGER NOT NULL,
    "TransactionReference" TEXT NULL,
    "UtrNumber" TEXT NULL,
    "IdempotencyKey" TEXT NULL,
    "Notes" TEXT NULL,
    "PaidAtUtc" TEXT NOT NULL,
    "CreatedAtUtc" TEXT NOT NULL,
    "UpdatedAtUtc" TEXT NULL,
    "CreatedBy" TEXT NULL,
    "UpdatedBy" TEXT NULL,
    "IsDeleted" INTEGER NOT NULL,
    CONSTRAINT "FK_Payments_Customers_CustomerId" FOREIGN KEY ("CustomerId") REFERENCES "Customers" ("Id") ON DELETE CASCADE,
    CONSTRAINT "FK_Payments_Orders_OrderId" FOREIGN KEY ("OrderId") REFERENCES "Orders" ("Id") ON DELETE SET NULL
);

CREATE TABLE "ReturnOrders" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_ReturnOrders" PRIMARY KEY,
    "ReturnNumber" TEXT NOT NULL,
    "OrderId" TEXT NOT NULL,
    "CustomerId" TEXT NOT NULL,
    "Reason" TEXT NOT NULL,
    "Status" TEXT NOT NULL,
    "InspectionNotes" TEXT NULL,
    "IsSellable" INTEGER NOT NULL,
    "RefundAmount" INTEGER NOT NULL,
    "RequestedAtUtc" TEXT NOT NULL,
    "InspectedAtUtc" TEXT NULL,
    "CreatedAtUtc" TEXT NOT NULL,
    "UpdatedAtUtc" TEXT NULL,
    "CreatedBy" TEXT NULL,
    "UpdatedBy" TEXT NULL,
    "IsDeleted" INTEGER NOT NULL,
    CONSTRAINT "FK_ReturnOrders_Customers_CustomerId" FOREIGN KEY ("CustomerId") REFERENCES "Customers" ("Id") ON DELETE CASCADE,
    CONSTRAINT "FK_ReturnOrders_Orders_OrderId" FOREIGN KEY ("OrderId") REFERENCES "Orders" ("Id") ON DELETE CASCADE
);

CREATE TABLE "GoodsReceipts" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_GoodsReceipts" PRIMARY KEY,
    "ReceiptNumber" TEXT NOT NULL,
    "PurchaseOrderId" TEXT NOT NULL,
    "WarehouseId" TEXT NOT NULL,
    "SupplierId" TEXT NOT NULL,
    "ReceivedDateUtc" TEXT NOT NULL,
    "Notes" TEXT NULL,
    "CreatedAtUtc" TEXT NOT NULL,
    "UpdatedAtUtc" TEXT NULL,
    "CreatedBy" TEXT NULL,
    "UpdatedBy" TEXT NULL,
    "IsDeleted" INTEGER NOT NULL,
    CONSTRAINT "FK_GoodsReceipts_PurchaseOrders_PurchaseOrderId" FOREIGN KEY ("PurchaseOrderId") REFERENCES "PurchaseOrders" ("Id") ON DELETE CASCADE,
    CONSTRAINT "FK_GoodsReceipts_Suppliers_SupplierId" FOREIGN KEY ("SupplierId") REFERENCES "Suppliers" ("Id") ON DELETE CASCADE,
    CONSTRAINT "FK_GoodsReceipts_Warehouses_WarehouseId" FOREIGN KEY ("WarehouseId") REFERENCES "Warehouses" ("Id") ON DELETE CASCADE
);

CREATE TABLE "PurchaseOrderItems" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_PurchaseOrderItems" PRIMARY KEY,
    "PurchaseOrderId" TEXT NOT NULL,
    "ProductId" TEXT NOT NULL,
    "ProductNameSnapshot" TEXT NOT NULL,
    "SKUSnapshot" TEXT NOT NULL,
    "UnitPrice" INTEGER NOT NULL,
    "QuantityOrdered" INTEGER NOT NULL,
    "QuantityReceived" INTEGER NOT NULL,
    "LineTotal" INTEGER NOT NULL,
    "CreatedAtUtc" TEXT NOT NULL,
    "UpdatedAtUtc" TEXT NULL,
    "CreatedBy" TEXT NULL,
    "UpdatedBy" TEXT NULL,
    "IsDeleted" INTEGER NOT NULL,
    CONSTRAINT "FK_PurchaseOrderItems_Products_ProductId" FOREIGN KEY ("ProductId") REFERENCES "Products" ("Id") ON DELETE CASCADE,
    CONSTRAINT "FK_PurchaseOrderItems_PurchaseOrders_PurchaseOrderId" FOREIGN KEY ("PurchaseOrderId") REFERENCES "PurchaseOrders" ("Id") ON DELETE CASCADE
);

CREATE TABLE "Refunds" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_Refunds" PRIMARY KEY,
    "RefundNumber" TEXT NOT NULL,
    "OrderId" TEXT NOT NULL,
    "PaymentId" TEXT NULL,
    "Amount" INTEGER NOT NULL,
    "Reason" TEXT NOT NULL,
    "Method" INTEGER NOT NULL,
    "Status" TEXT NOT NULL,
    "Reference" TEXT NULL,
    "IdempotencyKey" TEXT NULL,
    "ProcessedAtUtc" TEXT NOT NULL,
    "CreatedAtUtc" TEXT NOT NULL,
    "UpdatedAtUtc" TEXT NULL,
    "CreatedBy" TEXT NULL,
    "UpdatedBy" TEXT NULL,
    "IsDeleted" INTEGER NOT NULL,
    CONSTRAINT "FK_Refunds_Orders_OrderId" FOREIGN KEY ("OrderId") REFERENCES "Orders" ("Id") ON DELETE CASCADE,
    CONSTRAINT "FK_Refunds_Payments_PaymentId" FOREIGN KEY ("PaymentId") REFERENCES "Payments" ("Id")
);

CREATE TABLE "ReturnOrderItems" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_ReturnOrderItems" PRIMARY KEY,
    "ReturnOrderId" TEXT NOT NULL,
    "ProductId" TEXT NOT NULL,
    "Quantity" INTEGER NOT NULL,
    "UnitPrice" INTEGER NOT NULL,
    "IsDamaged" INTEGER NOT NULL,
    "ConditionNotes" TEXT NULL,
    "CreatedAtUtc" TEXT NOT NULL,
    "UpdatedAtUtc" TEXT NULL,
    "CreatedBy" TEXT NULL,
    "UpdatedBy" TEXT NULL,
    "IsDeleted" INTEGER NOT NULL,
    CONSTRAINT "FK_ReturnOrderItems_Products_ProductId" FOREIGN KEY ("ProductId") REFERENCES "Products" ("Id") ON DELETE CASCADE,
    CONSTRAINT "FK_ReturnOrderItems_ReturnOrders_ReturnOrderId" FOREIGN KEY ("ReturnOrderId") REFERENCES "ReturnOrders" ("Id") ON DELETE CASCADE
);

CREATE TABLE "GoodsReceiptItems" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_GoodsReceiptItems" PRIMARY KEY,
    "GoodsReceiptId" TEXT NOT NULL,
    "PurchaseOrderItemId" TEXT NOT NULL,
    "ProductId" TEXT NOT NULL,
    "QuantityReceived" INTEGER NOT NULL,
    "UnitPrice" INTEGER NOT NULL,
    "LineTotal" INTEGER NOT NULL,
    "CreatedAtUtc" TEXT NOT NULL,
    "UpdatedAtUtc" TEXT NULL,
    "CreatedBy" TEXT NULL,
    "UpdatedBy" TEXT NULL,
    "IsDeleted" INTEGER NOT NULL,
    CONSTRAINT "FK_GoodsReceiptItems_GoodsReceipts_GoodsReceiptId" FOREIGN KEY ("GoodsReceiptId") REFERENCES "GoodsReceipts" ("Id") ON DELETE CASCADE,
    CONSTRAINT "FK_GoodsReceiptItems_Products_ProductId" FOREIGN KEY ("ProductId") REFERENCES "Products" ("Id") ON DELETE CASCADE
);

CREATE TABLE "SupplierBills" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_SupplierBills" PRIMARY KEY,
    "BillNumber" TEXT NOT NULL,
    "SupplierId" TEXT NOT NULL,
    "PurchaseOrderId" TEXT NULL,
    "GoodsReceiptId" TEXT NULL,
    "Subtotal" INTEGER NOT NULL,
    "Tax" INTEGER NOT NULL,
    "Discount" INTEGER NOT NULL,
    "Total" INTEGER NOT NULL,
    "PaidAmount" INTEGER NOT NULL,
    "BalanceAmount" INTEGER NOT NULL,
    "DueDateUtc" TEXT NOT NULL,
    "Status" TEXT NOT NULL,
    "CreatedAtUtc" TEXT NOT NULL,
    "UpdatedAtUtc" TEXT NULL,
    "CreatedBy" TEXT NULL,
    "UpdatedBy" TEXT NULL,
    "IsDeleted" INTEGER NOT NULL,
    CONSTRAINT "FK_SupplierBills_GoodsReceipts_GoodsReceiptId" FOREIGN KEY ("GoodsReceiptId") REFERENCES "GoodsReceipts" ("Id"),
    CONSTRAINT "FK_SupplierBills_PurchaseOrders_PurchaseOrderId" FOREIGN KEY ("PurchaseOrderId") REFERENCES "PurchaseOrders" ("Id"),
    CONSTRAINT "FK_SupplierBills_Suppliers_SupplierId" FOREIGN KEY ("SupplierId") REFERENCES "Suppliers" ("Id") ON DELETE CASCADE
);

CREATE INDEX "IX_AspNetRoleClaims_RoleId" ON "AspNetRoleClaims" ("RoleId");

CREATE UNIQUE INDEX "RoleNameIndex" ON "AspNetRoles" ("NormalizedName");

CREATE INDEX "IX_AspNetUserClaims_UserId" ON "AspNetUserClaims" ("UserId");

CREATE INDEX "IX_AspNetUserLogins_UserId" ON "AspNetUserLogins" ("UserId");

CREATE INDEX "IX_AspNetUserRoles_RoleId" ON "AspNetUserRoles" ("RoleId");

CREATE INDEX "EmailIndex" ON "AspNetUsers" ("NormalizedEmail");

CREATE UNIQUE INDEX "UserNameIndex" ON "AspNetUsers" ("NormalizedUserName");

CREATE INDEX "IX_AuditLogs_Action" ON "AuditLogs" ("Action");

CREATE INDEX "IX_AuditLogs_CorrelationId" ON "AuditLogs" ("CorrelationId");

CREATE INDEX "IX_AuditLogs_EntityType" ON "AuditLogs" ("EntityType");

CREATE INDEX "IX_AuditLogs_Module" ON "AuditLogs" ("Module");

CREATE INDEX "IX_AuditLogs_TimestampUtc" ON "AuditLogs" ("TimestampUtc");

CREATE INDEX "IX_AuditLogs_UserId" ON "AuditLogs" ("UserId");

CREATE UNIQUE INDEX "IX_Brands_Slug" ON "Brands" ("Slug");

CREATE INDEX "IX_Categories_ParentCategoryId" ON "Categories" ("ParentCategoryId");

CREATE UNIQUE INDEX "IX_Categories_Slug" ON "Categories" ("Slug");

CREATE INDEX "IX_CustomerAddresses_CustomerId" ON "CustomerAddresses" ("CustomerId");

CREATE UNIQUE INDEX "IX_Customers_CustomerCode" ON "Customers" ("CustomerCode");

CREATE UNIQUE INDEX "IX_Customers_Email" ON "Customers" ("Email");

CREATE INDEX "IX_Customers_Phone" ON "Customers" ("Phone");

CREATE INDEX "IX_Expenses_Category" ON "Expenses" ("Category");

CREATE INDEX "IX_Expenses_ExpenseDateUtc" ON "Expenses" ("ExpenseDateUtc");

CREATE UNIQUE INDEX "IX_Expenses_ExpenseNumber" ON "Expenses" ("ExpenseNumber");

CREATE INDEX "IX_GiftBoxItems_ComponentProductId" ON "GiftBoxItems" ("ComponentProductId");

CREATE INDEX "IX_GiftBoxItems_ParentProductId" ON "GiftBoxItems" ("ParentProductId");

CREATE INDEX "IX_GoodsReceiptItems_GoodsReceiptId" ON "GoodsReceiptItems" ("GoodsReceiptId");

CREATE INDEX "IX_GoodsReceiptItems_ProductId" ON "GoodsReceiptItems" ("ProductId");

CREATE INDEX "IX_GoodsReceipts_PurchaseOrderId" ON "GoodsReceipts" ("PurchaseOrderId");

CREATE INDEX "IX_GoodsReceipts_SupplierId" ON "GoodsReceipts" ("SupplierId");

CREATE INDEX "IX_GoodsReceipts_WarehouseId" ON "GoodsReceipts" ("WarehouseId");

CREATE INDEX "IX_Invoices_CustomerId" ON "Invoices" ("CustomerId");

CREATE UNIQUE INDEX "IX_Invoices_InvoiceNumber" ON "Invoices" ("InvoiceNumber");

CREATE INDEX "IX_Invoices_OrderId" ON "Invoices" ("OrderId");

CREATE INDEX "IX_OrderItems_OrderId" ON "OrderItems" ("OrderId");

CREATE INDEX "IX_OrderItems_ProductId" ON "OrderItems" ("ProductId");

CREATE INDEX "IX_Orders_CustomerId" ON "Orders" ("CustomerId");

CREATE UNIQUE INDEX "IX_Orders_OrderNumber" ON "Orders" ("OrderNumber");

CREATE INDEX "IX_Orders_OrderStatus" ON "Orders" ("OrderStatus");

CREATE INDEX "IX_Orders_PlacedAtUtc" ON "Orders" ("PlacedAtUtc");

CREATE INDEX "IX_Orders_WarehouseId" ON "Orders" ("WarehouseId");

CREATE INDEX "IX_OrderStatusHistories_OrderId" ON "OrderStatusHistories" ("OrderId");

CREATE INDEX "IX_OutboxMessages_OccurredOnUtc" ON "OutboxMessages" ("OccurredOnUtc");

CREATE INDEX "IX_OutboxMessages_ProcessedOnUtc" ON "OutboxMessages" ("ProcessedOnUtc");

CREATE INDEX "IX_Payments_CustomerId" ON "Payments" ("CustomerId");

CREATE INDEX "IX_Payments_OrderId" ON "Payments" ("OrderId");

CREATE UNIQUE INDEX "IX_Payments_PaymentNumber" ON "Payments" ("PaymentNumber");

CREATE INDEX "IX_ProductCategories_CategoryId" ON "ProductCategories" ("CategoryId");

CREATE INDEX "IX_ProductCategories_ProductId" ON "ProductCategories" ("ProductId");

CREATE INDEX "IX_ProductImages_ProductId" ON "ProductImages" ("ProductId");

CREATE INDEX "IX_ProductReviews_ProductId" ON "ProductReviews" ("ProductId");

CREATE INDEX "IX_Products_BrandId" ON "Products" ("BrandId");

CREATE INDEX "IX_Products_CategoryId" ON "Products" ("CategoryId");

CREATE INDEX "IX_Products_IsActive" ON "Products" ("IsActive");

CREATE INDEX "IX_Products_IsBestSeller" ON "Products" ("IsBestSeller");

CREATE INDEX "IX_Products_IsFeatured" ON "Products" ("IsFeatured");

CREATE UNIQUE INDEX "IX_Products_SKU" ON "Products" ("SKU");

CREATE UNIQUE INDEX "IX_Products_Slug" ON "Products" ("Slug");

CREATE INDEX "IX_ProductVariants_ProductId" ON "ProductVariants" ("ProductId");

CREATE UNIQUE INDEX "IX_Promotions_Code" ON "Promotions" ("Code");

CREATE INDEX "IX_PurchaseOrderItems_ProductId" ON "PurchaseOrderItems" ("ProductId");

CREATE INDEX "IX_PurchaseOrderItems_PurchaseOrderId" ON "PurchaseOrderItems" ("PurchaseOrderId");

CREATE UNIQUE INDEX "IX_PurchaseOrders_PoNumber" ON "PurchaseOrders" ("PoNumber");

CREATE INDEX "IX_PurchaseOrders_SupplierId" ON "PurchaseOrders" ("SupplierId");

CREATE INDEX "IX_PurchaseOrders_WarehouseId" ON "PurchaseOrders" ("WarehouseId");

CREATE INDEX "IX_Refunds_OrderId" ON "Refunds" ("OrderId");

CREATE INDEX "IX_Refunds_PaymentId" ON "Refunds" ("PaymentId");

CREATE INDEX "IX_ReturnOrderItems_ProductId" ON "ReturnOrderItems" ("ProductId");

CREATE INDEX "IX_ReturnOrderItems_ReturnOrderId" ON "ReturnOrderItems" ("ReturnOrderId");

CREATE INDEX "IX_ReturnOrders_CustomerId" ON "ReturnOrders" ("CustomerId");

CREATE INDEX "IX_ReturnOrders_OrderId" ON "ReturnOrders" ("OrderId");

CREATE UNIQUE INDEX "IX_StockItems_ProductId_WarehouseId" ON "StockItems" ("ProductId", "WarehouseId");

CREATE INDEX "IX_StockItems_WarehouseId" ON "StockItems" ("WarehouseId");

CREATE INDEX "IX_StockMovements_CreatedAtUtc" ON "StockMovements" ("CreatedAtUtc");

CREATE INDEX "IX_StockMovements_ProductId" ON "StockMovements" ("ProductId");

CREATE INDEX "IX_StockMovements_WarehouseId" ON "StockMovements" ("WarehouseId");

CREATE INDEX "IX_SupplierBills_GoodsReceiptId" ON "SupplierBills" ("GoodsReceiptId");

CREATE INDEX "IX_SupplierBills_PurchaseOrderId" ON "SupplierBills" ("PurchaseOrderId");

CREATE INDEX "IX_SupplierBills_SupplierId" ON "SupplierBills" ("SupplierId");

CREATE UNIQUE INDEX "IX_Suppliers_Code" ON "Suppliers" ("Code");

CREATE INDEX "IX_SystemSettings_Group" ON "SystemSettings" ("Group");

CREATE UNIQUE INDEX "IX_SystemSettings_Key" ON "SystemSettings" ("Key");

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260831202307_InitialCreate', '9.0.2');

COMMIT;

