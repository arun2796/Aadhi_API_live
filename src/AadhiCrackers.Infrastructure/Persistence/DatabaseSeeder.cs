using AadhiCrackers.Domain.Entities;
using AadhiCrackers.Domain.Enums;
using AadhiCrackers.Domain.ValueObjects;
using AadhiCrackers.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace AadhiCrackers.Infrastructure.Persistence;

public static class DatabaseSeeder
{
    private const string DefaultAdminEmail = "admin@aadhicrackers.com";
    private const string DefaultAdminPassword = "Admin@123";

    /// <summary>The one delivery note the storefront shows. Mirrors OrderService.DefaultTransportNote.</summary>
    private const string TransportNoteDefault = "Freight is payable directly to the transport company when you collect the parcel.";

    private const string DeprecatedChargeDescription =
        "DEPRECATED — not read by any code. The store charges nothing for delivery: goods travel by lorry and the customer pays the transport company directly on collection. Pinned to 0.00.";

    /// <summary>
    /// Legacy delivery-charge settings. Kept seeded (the public settings whitelist exposes the
    /// Shipping./Delivery. prefixes and old storefront builds still read them) but forced to
    /// "0.00" on every boot so a stale non-zero value can never resurface.
    /// </summary>
    private static readonly string[] DeprecatedDeliveryChargeKeys =
    {
        "Shipping.FreeShippingThreshold",
        "Shipping.StandardCharge",
        "Delivery.StandardCharge",
        "Delivery.ExpressCharge",
        "Delivery.GodownPickupCharge",
        "Delivery.ParcelServiceCharge"
    };


    public static async Task SeedAsync(
        AadhiDbContext context,
        UserManager<ApplicationUser> userManager,
        RoleManager<ApplicationRole> roleManager,
        ILogger logger,
        IConfiguration configuration,
        bool isDevelopment)
    {
        try
        {
            var includeDemoData = ShouldIncludeDemoData(configuration, isDevelopment);

            // ───────────────────────── ESSENTIALS (always seeded) ─────────────────────────

            // 1. Seed Roles
            foreach (var roleName in AppRoles.All)
            {
                if (!await roleManager.RoleExistsAsync(roleName))
                {
                    await roleManager.CreateAsync(new ApplicationRole(roleName, $"Default {roleName} role"));
                }
            }

            var adminEmail = configuration["Seeding:AdminEmail"];
            if (string.IsNullOrWhiteSpace(adminEmail))
            {
                adminEmail = DefaultAdminEmail;
            }

            var configuredAdminPassword = configuration["Seeding:AdminPassword"];
            var usesDefaultAdminPassword = string.IsNullOrWhiteSpace(configuredAdminPassword);
            var adminPassword = usesDefaultAdminPassword ? DefaultAdminPassword : configuredAdminPassword!;

            var adminUser = await userManager.FindByEmailAsync(adminEmail);
            if (adminUser == null)
            {
                if (usesDefaultAdminPassword && !isDevelopment)
                {
                    logger.LogWarning("⚠️ SECURITY WARNING: Default admin password in use — set Seeding__AdminPassword");
                }

                adminUser = new ApplicationUser
                {
                    UserName = adminEmail,
                    Email = adminEmail,
                    FirstName = "Arun",
                    LastName = "Kumar",
                    CustomerCode = "EMP-001",
                    PhoneNumber = "9876543210",
                    EmailConfirmed = true,
                    IsActive = true,
                    CreatedAtUtc = DateTime.UtcNow
                };

                var res = await userManager.CreateAsync(adminUser, adminPassword);
                if (res.Succeeded)
                {
                    await userManager.AddToRoleAsync(adminUser, AppRoles.SuperAdmin);
                }
            }


            // 4. Seed System Settings (idempotent — inserts missing keys, never overwrites existing values)
            var settingDefaults = new SystemSetting[]
            {
                new() { Key = "Store.BusinessName", Value = "AADHI CRACKERS", Group = "Store", Description = "Official Business Name" },
                new() { Key = "Store.Tagline", Value = "Celebrate Every Moment", Group = "Store", Description = "Brand Tagline" },
                new() { Key = "Store.Phone", Value = "+91 98765 43210", Group = "Store", Description = "Contact Phone" },
                new() { Key = "Store.Email", Value = "support@aadhicrackers.com", Group = "Store", Description = "Support Email" },
                new() { Key = "Store.Address", Value = "123, West Street, Sivanandapuram, Coimbatore, Tamil Nadu - 641012", Group = "Store", Description = "Physical Store Address" },
                new() { Key = "Tax.GstRate", Value = "18.00", Group = "Tax", Description = "Default GST Rate for Fireworks" },
                // ── Delivery: the store never charges for delivery ──────────────────────────
                // Goods travel by lorry and the customer settles the freight directly with the
                // transport company on collection, so every charge key below is DEPRECATED,
                // pinned to 0.00 and read by nothing. They stay seeded only because the public
                // settings whitelist exposes the whole "Shipping."/"Delivery." prefix and older
                // storefront builds still look them up.
                new() { Key = "Shipping.FreeShippingThreshold", Value = "0.00", Group = "Shipping", Description = DeprecatedChargeDescription + " There is no free-shipping threshold — delivery is never charged at all." },
                new() { Key = "Shipping.StandardCharge", Value = "0.00", Group = "Shipping", Description = DeprecatedChargeDescription },
                new() { Key = "Delivery.StandardCharge", Value = "0.00", Group = "Shipping", Description = DeprecatedChargeDescription },
                new() { Key = "Delivery.ExpressCharge", Value = "0.00", Group = "Shipping", Description = DeprecatedChargeDescription },
                new() { Key = "Delivery.GodownPickupCharge", Value = "0.00", Group = "Shipping", Description = DeprecatedChargeDescription },
                new() { Key = "Delivery.ParcelServiceCharge", Value = "0.00", Group = "Shipping", Description = DeprecatedChargeDescription },
                new() { Key = "Delivery.TransportNote", Value = TransportNoteDefault, Group = "Shipping", Description = "Customer-facing note returned by GET /orders/delivery-options explaining that freight is settled with the transport company." },
                new() { Key = "Delivery.EtaMinDays", Value = "7", Group = "Shipping", Description = "Minimum transport delivery ETA in days (GET /orders/delivery-options)." },
                new() { Key = "Delivery.EtaMaxDays", Value = "14", Group = "Shipping", Description = "Maximum transport delivery ETA in days (GET /orders/delivery-options)." },
                new() { Key = "Order.PackingChargePercent", Value = "1.5", Group = "Order", Description = "Packing charges billed as a percentage of the items subtotal" },
                new() { Key = "Payment.BankName", Value = "AXIS BANK LTD", Group = "Payment", Description = "Bank name shown on the customer payment screen" },
                new() { Key = "Payment.AccountName", Value = "AADHI CRACKERS", Group = "Payment", Description = "Bank account holder name shown on the customer payment screen" },
                new() { Key = "Payment.AccountNumber", Value = "926020003006172", Group = "Payment", Description = "Bank account number shown on the customer payment screen" },
                new() { Key = "Payment.IfscCode", Value = "UTIB0000089", Group = "Payment", Description = "Bank IFSC code shown on the customer payment screen" },
                new() { Key = "RateLimiting.Enabled", Value = "true", Group = "Security", Description = "Enable API Rate Limiting" }
            };

            var addedSettings = false;
            foreach (var setting in settingDefaults)
            {
                var existing = await context.SystemSettings
                    .IgnoreQueryFilters()
                    .FirstOrDefaultAsync(s => s.Key == setting.Key);
                if (existing == null)
                {
                    context.SystemSettings.Add(setting);
                    addedSettings = true;
                }
                else if (DeprecatedDeliveryChargeKeys.Contains(setting.Key))
                {
                    // Repair pass: a stale non-zero charge (or a stale description) from an older
                    // build must never survive a boot — nothing is charged for delivery any more.
                    if (existing.Value != "0.00" || existing.Description != setting.Description)
                    {
                        existing.Value = "0.00";
                        existing.Description = setting.Description;
                        existing.UpdatedAtUtc = DateTime.UtcNow;
                        addedSettings = true;
                    }
                }
            }

            if (addedSettings)
            {
                await context.SaveChangesAsync();
            }

            // Zero out any delivery charges on existing orders (Sivakasi crackers have no delivery charges)
            var ordersWithShipping = await context.Orders
                .IgnoreQueryFilters()
                .ToListAsync();

            var shippingUpdated = false;
            foreach (var o in ordersWithShipping)
            {
                // Packing charges are a real billed line and must survive this repair pass.
                var expectedTotal = Math.Max(0m, o.ItemsSubtotal.ToDecimal() - o.Discount.ToDecimal() + o.Tax.ToDecimal() + o.PackingCharges.ToDecimal());
                if (o.ShippingCharge.ToDecimal() > 0 || o.GrandTotal.ToDecimal() != expectedTotal)
                {
                    o.ShippingCharge = Money.Zero();
                    o.GrandTotal = Money.FromDecimal(expectedTotal);
                    shippingUpdated = true;
                }
            }

            if (shippingUpdated)
            {
                await context.SaveChangesAsync();
                logger.LogInformation("Zeroed out delivery charges on existing orders.");
            }

            // ── Repair: strip fabricated LR / waybill numbers ────────────────────────────
            // Orders used to be stamped with TrackingNumber = "TRK-########" at CREATION time,
            // invented by the server. The storefront prints it as "LR / Waybill — show this at the
            // transport office", and it pre-fills the admin's Quick Dispatch field, so a made-up
            // number could be dispatched with. Creation no longer mints one; this clears the rows
            // that already carry one. Deliberately conservative and idempotent:
            //   * only the fabricated "TRK-" pattern is touched — a real LR an admin typed in
            //     never matches it;
            //   * only orders that were never dispatched (no carrier recorded, and not
            //     Shipped/OutForDelivery/Delivered/Returned) — a shipped order's real number
            //     is never mutated;
            //   * every row cleared is logged individually with its old value.
            var fabricatedLrOrders = await context.Orders
                .IgnoreQueryFilters()
                .Where(o => o.TrackingNumber != null
                            && o.TrackingNumber.StartsWith("TRK-")
                            && (o.CarrierName == null || o.CarrierName == "")
                            && o.OrderStatus != OrderStatus.Shipped
                            && o.OrderStatus != OrderStatus.OutForDelivery
                            && o.OrderStatus != OrderStatus.Delivered
                            && o.OrderStatus != OrderStatus.Returned)
                .ToListAsync();

            if (fabricatedLrOrders.Count > 0)
            {
                foreach (var o in fabricatedLrOrders)
                {
                    logger.LogWarning(
                        "Clearing fabricated LR/waybill '{TrackingNumber}' from order {OrderNumber} (status {Status}, no carrier — never dispatched).",
                        o.TrackingNumber, o.OrderNumber, o.OrderStatus);
                    o.TrackingNumber = null;
                    o.UpdatedAtUtc = DateTime.UtcNow;
                    o.UpdatedBy = "system:fabricated-lr-cleanup";
                }

                await context.SaveChangesAsync();
                logger.LogWarning(
                    "Removed {Count} fabricated TRK- LR/waybill number(s) from orders that were never dispatched.",
                    fabricatedLrOrders.Count);
            }

            // ───────────────────────── DEMO DATA (gated) ─────────────────────────
            if (includeDemoData)
            {
                logger.LogInformation("Seeding demo data (Seeding:IncludeDemoData=true).");
                await SeedDemoDataAsync(context, userManager);
            }
            else
            {
                logger.LogInformation("Demo data seeding skipped (Seeding:IncludeDemoData=false) — essentials only.");
            }

            logger.LogInformation("✅ Database seed completed successfully.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "❌ Error occurred during database seeding.");
            throw;
        }
    }

    /// <summary>
    /// Explicit config value always wins; otherwise demo data defaults to true only in Development.
    /// </summary>
    private static bool ShouldIncludeDemoData(IConfiguration configuration, bool isDevelopment)
    {
        var configured = configuration["Seeding:IncludeDemoData"];
        if (bool.TryParse(configured, out var explicitValue))
        {
            return explicitValue;
        }

        return isDevelopment;
    }

    private static async Task SeedDemoDataAsync(
        AadhiDbContext context,
        UserManager<ApplicationUser> userManager)
    {
        // D1. Seed Sample Customer User
        var customerEmail = "customer@aadhicrackers.com";
            var customerUser = await userManager.FindByEmailAsync(customerEmail);
            Customer? customerEntity = null;

            if (customerUser == null)
            {
                customerUser = new ApplicationUser
                {
                    UserName = customerEmail,
                    Email = customerEmail,
                    FirstName = "Ramesh",
                    LastName = "Kumar",
                    CustomerCode = "CUST-2026-0001",
                    PhoneNumber = "9876543211",
                    EmailConfirmed = true,
                    IsActive = true,
                    CreatedAtUtc = DateTime.UtcNow
                };

                var res = await userManager.CreateAsync(customerUser, "Customer@123");
                if (res.Succeeded)
                {
                    await userManager.AddToRoleAsync(customerUser, AppRoles.Customer);

                    customerEntity = new Customer
                    {
                        UserId = customerUser.Id,
                        CustomerCode = customerUser.CustomerCode,
                        FirstName = customerUser.FirstName,
                        LastName = customerUser.LastName,
                        Email = customerUser.Email,
                        Phone = customerUser.PhoneNumber,
                        IsActive = true
                    };

                    customerEntity.Addresses.Add(new CustomerAddress
                    {
                        CustomerId = customerEntity.Id,
                        AddressType = AddressType.Both,
                        IsDefault = true,
                        Address = new Address(
                            "Ramesh Kumar",
                            "9876543211",
                            "123, West Street, Sivanandapuram",
                            "Opposite City Hospital",
                            "Coimbatore",
                            "Tamil Nadu",
                            "641012",
                            "India")
                    });

                    context.Customers.Add(customerEntity);
                    await context.SaveChangesAsync();
                }
            }
            else
            {
                customerEntity = await context.Customers.FirstOrDefaultAsync(c => c.Email == customerEmail);
            }


            // D4. Seed Demo Brands
            var aadhiBrand = await context.Brands.FirstOrDefaultAsync(b => b.Slug == "aadhi-crackers");
            if (aadhiBrand == null)
            {
                aadhiBrand = new Brand
                {
                    Name = "AADHI CRACKERS",
                    Slug = "aadhi-crackers",
                    Description = "Premium, certified authentic Sivakasi fireworks with maximum brightness and superior safety standard.",
                    LogoUrl = "/images/brands/aadhi-logo.png",
                    IsActive = true
                };
                var standardBrand = new Brand
                {
                    Name = "Standard Fireworks",
                    Slug = "standard-fireworks",
                    Description = "Trusted legacy fireworks brand since 1942.",
                    IsActive = true
                };
                var vanithaBrand = new Brand
                {
                    Name = "Vanitha Fireworks",
                    Slug = "vanitha-fireworks",
                    Description = "Renowned for ground sparklers and fancy crackers.",
                    IsActive = true
                };

                context.Brands.AddRange(aadhiBrand, standardBrand, vanithaBrand);
                await context.SaveChangesAsync();
            }

            // D5. Seed Demo Categories
            if (!await context.Categories.AnyAsync())
            {
                var giftBoxesCat = new Category
                {
                    Name = "Gift Boxes",
                    Slug = "gift-boxes",
                    Description = "Curated assortment gift boxes perfect for families, children, and corporate gifting.",
                    ImageUrl = "https://images.unsplash.com/photo-1513151233558-d860c5398176?w=600&auto=format&fit=crop&q=80",
                    DisplayOrder = 1,
                    IsActive = true
                };
                var comboOffersCat = new Category
                {
                    Name = "Combo Offers",
                    Slug = "combo-offers",
                    Description = "Mega savings festive value combo packs.",
                    ImageUrl = "https://images.unsplash.com/photo-1467810563316-b5476525c0f9?w=600&auto=format&fit=crop&q=80",
                    DisplayOrder = 2,
                    IsActive = true
                };
                var sparklersCat = new Category
                {
                    Name = "Sparklers",
                    Slug = "sparklers",
                    Description = "Electric, gold, and color sparklers with low smoke and longer burning time.",
                    ImageUrl = "https://images.unsplash.com/photo-1498931299472-f7a63a5a1cfa?w=600&auto=format&fit=crop&q=80",
                    DisplayOrder = 3,
                    IsActive = true
                };
                var groundChakkarCat = new Category
                {
                    Name = "Ground Chakkar",
                    Slug = "ground-chakkar",
                    Description = "Fast spinning wheels with glittering sparks.",
                    ImageUrl = "https://images.unsplash.com/photo-1498931299472-f7a63a5a1cfa?w=600&auto=format&fit=crop&q=80",
                    DisplayOrder = 4,
                    IsActive = true
                };
                var flowerPotsCat = new Category
                {
                    Name = "Flower Pots",
                    Slug = "flower-pots",
                    Description = "Color fountains, Ashoka pots, and glittering cascades.",
                    ImageUrl = "https://images.unsplash.com/photo-1514565131-fce0801e5785?w=600&auto=format&fit=crop&q=80",
                    DisplayOrder = 5,
                    IsActive = true
                };
                var rocketsCat = new Category
                {
                    Name = "Rockets",
                    Slug = "rockets",
                    Description = "High altitude whistling and bursting rockets.",
                    ImageUrl = "https://images.unsplash.com/photo-1514565131-fce0801e5785?w=600&auto=format&fit=crop&q=80",
                    DisplayOrder = 6,
                    IsActive = true
                };
                var aerialShotsCat = new Category
                {
                    Name = "Aerial Shots",
                    Slug = "aerial-shots",
                    Description = "Multi-shot repeater fireworks creating magical sky patterns.",
                    ImageUrl = "https://images.unsplash.com/photo-1467810563316-b5476525c0f9?w=600&auto=format&fit=crop&q=80",
                    DisplayOrder = 7,
                    IsActive = true
                };

                context.Categories.AddRange(giftBoxesCat, comboOffersCat, sparklersCat, groundChakkarCat, flowerPotsCat, rocketsCat, aerialShotsCat);
                await context.SaveChangesAsync();

                // D6. Seed Demo Products matching Screenshot visual design
                var products = new List<Product>
                {
                    new Product
                    {
                        SKU = "GB-DLX-001",
                        Name = "Aadhi Deluxe Gift Box",
                        Slug = "aadhi-deluxe-gift-box",
                        Description = "The quintessential festive assortment featuring 45 premium items: sparklers, ground chakkars, flower pots, whistling rockets, and fancy aerial shots. Packaged in a stunning festive gift box.",
                        ShortDescription = "45 Premium Items • Festive Assortment • Safe & Tested",
                        CategoryId = giftBoxesCat.Id,
                        BrandId = aadhiBrand.Id,
                        Price = Money.FromDecimal(2999m),
                        CompareAtPrice = Money.FromDecimal(4999m),
                        CostPrice = Money.FromDecimal(1800m),
                        TaxRate = 18m,
                        DiscountType = DiscountType.Percentage,
                        DiscountValue = 40m,
                        StockQuantity = 150,
                        ReservedQuantity = 10,
                        ReorderLevel = 30,
                        Unit = "Box",
                        WeightKg = 4.5m,
                        IsActive = true,
                        IsFeatured = true,
                        IsBestSeller = true,
                        IsNewArrival = false,
                        SafetyInformation = "Keep at least 5 meters distance. Use an incense stick to light. Keep water or sand bucket nearby."
                    },
                    new Product
                    {
                        SKU = "GB-MGA-002",
                        Name = "Mega Celebration Box",
                        Slug = "mega-celebration-box",
                        Description = "Our flagship Diwali celebration box including 62 premium items. Contains multi-color sparklers, deluxe flower pots, jumbo chakkars, 12-shot aerial repeaters, and whistling rockets. Guaranteed family delight!",
                        ShortDescription = "62 Premium Items • Longer Burning Time • Safe & Eco-Friendly • Perfect for All Celebrations",
                        CategoryId = giftBoxesCat.Id,
                        BrandId = aadhiBrand.Id,
                        Price = Money.FromDecimal(4499m),
                        CompareAtPrice = Money.FromDecimal(5999m),
                        CostPrice = Money.FromDecimal(2700m),
                        TaxRate = 18m,
                        DiscountType = DiscountType.Percentage,
                        DiscountValue = 25m,
                        StockQuantity = 95,
                        ReservedQuantity = 15,
                        ReorderLevel = 25,
                        Unit = "Box",
                        WeightKg = 7.0m,
                        IsActive = true,
                        IsFeatured = true,
                        IsBestSeller = true,
                        IsNewArrival = true,
                        SafetyInformation = "Always light under adult supervision. Do not hold fireworks in hand."
                    },
                    new Product
                    {
                        SKU = "GB-RYL-003",
                        Name = "Royal Premium Box",
                        Slug = "royal-premium-box",
                        Description = "An extravagant box packed with 55 luxury pyrotechnic items including 30-shot sky repeaters, tri-color flower pots, and gold sparklers.",
                        ShortDescription = "55 Luxury Pyros • Golden Sparks • 30-Shot Aerial",
                        CategoryId = giftBoxesCat.Id,
                        BrandId = aadhiBrand.Id,
                        Price = Money.FromDecimal(3999m),
                        CompareAtPrice = Money.FromDecimal(5499m),
                        CostPrice = Money.FromDecimal(2400m),
                        TaxRate = 18m,
                        DiscountType = DiscountType.Percentage,
                        DiscountValue = 27m,
                        StockQuantity = 110,
                        ReservedQuantity = 8,
                        ReorderLevel = 25,
                        Unit = "Box",
                        WeightKg = 6.2m,
                        IsActive = true,
                        IsFeatured = true,
                        IsBestSeller = false,
                        IsNewArrival = true
                    },
                    new Product
                    {
                        SKU = "GB-FST-004",
                        Name = "Festival Special Box",
                        Slug = "festival-special-box",
                        Description = "Value-packed 30-item box designed for lively family celebrations.",
                        ShortDescription = "30 Classic Items • Great Value • Certified Safe",
                        CategoryId = giftBoxesCat.Id,
                        BrandId = aadhiBrand.Id,
                        Price = Money.FromDecimal(1999m),
                        CompareAtPrice = Money.FromDecimal(2499m),
                        CostPrice = Money.FromDecimal(1200m),
                        TaxRate = 18m,
                        DiscountType = DiscountType.Percentage,
                        DiscountValue = 20m,
                        StockQuantity = 180,
                        ReservedQuantity = 20,
                        ReorderLevel = 40,
                        Unit = "Box",
                        WeightKg = 3.5m,
                        IsActive = true,
                        IsFeatured = true,
                        IsBestSeller = true,
                        IsNewArrival = false
                    },
                    new Product
                    {
                        SKU = "GB-FAM-005",
                        Name = "Family Fun Box",
                        Slug = "family-fun-box",
                        Description = "A delightful starter box containing 20 assorted items for young families.",
                        ShortDescription = "20 Items • Starter Pack • Low Smoke",
                        CategoryId = giftBoxesCat.Id,
                        BrandId = aadhiBrand.Id,
                        Price = Money.FromDecimal(999m),
                        CompareAtPrice = Money.FromDecimal(1599m),
                        CostPrice = Money.FromDecimal(600m),
                        TaxRate = 18m,
                        DiscountType = DiscountType.Percentage,
                        DiscountValue = 37m,
                        StockQuantity = 220,
                        ReservedQuantity = 12,
                        ReorderLevel = 50,
                        Unit = "Box",
                        WeightKg = 2.2m,
                        IsActive = true,
                        IsFeatured = false,
                        IsBestSeller = true,
                        IsNewArrival = false
                    },
                    new Product
                    {
                        SKU = "GB-KID-006",
                        Name = "Kids Special Box",
                        Slug = "kids-special-box",
                        Description = "Specially formulated low-decibel, high-color novelty fireworks for children.",
                        ShortDescription = "15 Low-Noise Items • Safe & Fun • Magic Pop-Pops",
                        CategoryId = giftBoxesCat.Id,
                        BrandId = aadhiBrand.Id,
                        Price = Money.FromDecimal(749m),
                        CompareAtPrice = Money.FromDecimal(999m),
                        CostPrice = Money.FromDecimal(450m),
                        TaxRate = 18m,
                        DiscountType = DiscountType.Percentage,
                        DiscountValue = 25m,
                        StockQuantity = 250,
                        ReservedQuantity = 14,
                        ReorderLevel = 50,
                        Unit = "Box",
                        WeightKg = 1.8m,
                        IsActive = true,
                        IsFeatured = false,
                        IsBestSeller = true,
                        IsNewArrival = false
                    },
                    new Product
                    {
                        SKU = "GB-SVR-007",
                        Name = "Super Saver Box",
                        Slug = "super-saver-box",
                        Description = "Economy celebration pack with 25 popular fireworks items.",
                        ShortDescription = "25 Essential Items • Budget Friendly",
                        CategoryId = giftBoxesCat.Id,
                        BrandId = aadhiBrand.Id,
                        Price = Money.FromDecimal(1469m),
                        CompareAtPrice = Money.FromDecimal(1899m),
                        CostPrice = Money.FromDecimal(900m),
                        TaxRate = 18m,
                        DiscountType = DiscountType.Percentage,
                        DiscountValue = 21m,
                        StockQuantity = 140,
                        ReservedQuantity = 5,
                        ReorderLevel = 30,
                        Unit = "Box",
                        WeightKg = 2.8m,
                        IsActive = true,
                        IsFeatured = false,
                        IsBestSeller = false,
                        IsNewArrival = false
                    },
                    new Product
                    {
                        SKU = "GB-ULT-008",
                        Name = "Ultimate Celebration Box",
                        Slug = "ultimate-celebration-box",
                        Description = "Grand master box packed with 85 premium items including grand sky repeaters and multi-layer flower pots.",
                        ShortDescription = "85 Grand Items • Sky Symphony • Maximum Delight",
                        CategoryId = giftBoxesCat.Id,
                        BrandId = aadhiBrand.Id,
                        Price = Money.FromDecimal(5999m),
                        CompareAtPrice = Money.FromDecimal(7499m),
                        CostPrice = Money.FromDecimal(3600m),
                        TaxRate = 18m,
                        DiscountType = DiscountType.Percentage,
                        DiscountValue = 20m,
                        StockQuantity = 60,
                        ReservedQuantity = 4,
                        ReorderLevel = 15,
                        Unit = "Box",
                        WeightKg = 9.5m,
                        IsActive = true,
                        IsFeatured = true,
                        IsBestSeller = false,
                        IsNewArrival = true
                    },
                    new Product
                    {
                        SKU = "SPK-10P-001",
                        Name = "10 Pcs Sparklers (Electric)",
                        Slug = "10-pcs-sparklers-electric",
                        Description = "15cm Electric sparklers offering brilliant white illumination with high burning duration.",
                        ShortDescription = "10 Pcs • 15cm Length • Low Smoke",
                        CategoryId = sparklersCat.Id,
                        BrandId = aadhiBrand.Id,
                        Price = Money.FromDecimal(45m),
                        CompareAtPrice = Money.FromDecimal(60m),
                        CostPrice = Money.FromDecimal(25m),
                        TaxRate = 18m,
                        DiscountType = DiscountType.Percentage,
                        DiscountValue = 25m,
                        StockQuantity = 560,
                        ReservedQuantity = 40,
                        ReorderLevel = 100,
                        Unit = "Pack",
                        WeightKg = 0.3m,
                        IsActive = true,
                        IsFeatured = true,
                        IsBestSeller = true,
                        IsNewArrival = false
                    },
                    new Product
                    {
                        SKU = "POT-BIG-001",
                        Name = "Flower Pots (Big)",
                        Slug = "flower-pots-big",
                        Description = "Classic golden shower flower pots throwing bright sparks up to 10 feet in the air.",
                        ShortDescription = "Pack of 10 • Golden Spark Fountain • 10ft Height",
                        CategoryId = flowerPotsCat.Id,
                        BrandId = aadhiBrand.Id,
                        Price = Money.FromDecimal(120m),
                        CompareAtPrice = Money.FromDecimal(150m),
                        CostPrice = Money.FromDecimal(70m),
                        TaxRate = 18m,
                        DiscountType = DiscountType.Percentage,
                        DiscountValue = 20m,
                        StockQuantity = 430,
                        ReservedQuantity = 30,
                        ReorderLevel = 80,
                        Unit = "Box (10 Pcs)",
                        WeightKg = 0.8m,
                        IsActive = true,
                        IsFeatured = true,
                        IsBestSeller = true,
                        IsNewArrival = false
                    },
                    new Product
                    {
                        SKU = "CHK-DLX-001",
                        Name = "Ground Chakkar Deluxe",
                        Slug = "ground-chakkar-deluxe",
                        Description = "High-speed spinning ground wheels with bright silver and red sparks.",
                        ShortDescription = "Pack of 10 • High Speed Rotation • Silver Glitter",
                        CategoryId = groundChakkarCat.Id,
                        BrandId = aadhiBrand.Id,
                        Price = Money.FromDecimal(80m),
                        CompareAtPrice = Money.FromDecimal(100m),
                        CostPrice = Money.FromDecimal(45m),
                        TaxRate = 18m,
                        DiscountType = DiscountType.Percentage,
                        DiscountValue = 20m,
                        StockQuantity = 410,
                        ReservedQuantity = 25,
                        ReorderLevel = 75,
                        Unit = "Box (10 Pcs)",
                        WeightKg = 0.5m,
                        IsActive = true,
                        IsFeatured = true,
                        IsBestSeller = true,
                        IsNewArrival = false
                    },
                    new Product
                    {
                        SKU = "AER-30S-001",
                        Name = "Aerial Shot - 30 Shots Multi Color",
                        Slug = "aerial-shot-30-shots",
                        Description = "Continuous 30 shots sky burst repeating with vibrant peony, palm, and crackling brocade effects.",
                        ShortDescription = "30 Continuous Shots • Multi-Color Sky Burst • Night Magic",
                        CategoryId = aerialShotsCat.Id,
                        BrandId = aadhiBrand.Id,
                        Price = Money.FromDecimal(1250m),
                        CompareAtPrice = Money.FromDecimal(1800m),
                        CostPrice = Money.FromDecimal(750m),
                        TaxRate = 18m,
                        DiscountType = DiscountType.Percentage,
                        DiscountValue = 30m,
                        StockQuantity = 45,
                        ReservedQuantity = 10,
                        ReorderLevel = 20,
                        Unit = "Piece",
                        WeightKg = 2.0m,
                        IsActive = true,
                        IsFeatured = true,
                        IsBestSeller = true,
                        IsNewArrival = false
                    },
                    new Product
                    {
                        SKU = "RCK-BMB-001",
                        Name = "Rocket Bomb (Pack of 10)",
                        Slug = "rocket-bomb-pack-of-10",
                        Description = "Classic high velocity rocket rising up to 100 feet followed by a sound bomb burst.",
                        ShortDescription = "Pack of 10 • High Ascent • Classic Boom",
                        CategoryId = rocketsCat.Id,
                        BrandId = aadhiBrand.Id,
                        Price = Money.FromDecimal(280m),
                        CompareAtPrice = Money.FromDecimal(350m),
                        CostPrice = Money.FromDecimal(160m),
                        TaxRate = 18m,
                        DiscountType = DiscountType.Percentage,
                        DiscountValue = 20m,
                        StockQuantity = 60,
                        ReservedQuantity = 8,
                        ReorderLevel = 25,
                        Unit = "Pack",
                        WeightKg = 0.6m,
                        IsActive = true,
                        IsFeatured = false,
                        IsBestSeller = true,
                        IsNewArrival = false
                    }
                };

                // Sample product mockup images matching visual theme
                var boxImages = new[]
                {
                    "https://images.unsplash.com/photo-1513151233558-d860c5398176?w=700&auto=format&fit=crop&q=80",
                    "https://images.unsplash.com/photo-1467810563316-b5476525c0f9?w=700&auto=format&fit=crop&q=80",
                    "https://images.unsplash.com/photo-1498931299472-f7a63a5a1cfa?w=700&auto=format&fit=crop&q=80"
                };

                int imgIdx = 0;
                foreach (var p in products)
                {
                    var imgUrl = boxImages[imgIdx % boxImages.Length];
                    p.Images.Add(new ProductImage
                    {
                        ProductId = p.Id,
                        Url = imgUrl,
                        AltText = p.Name,
                        SortOrder = 0,
                        IsPrimary = true
                    });
                    p.Images.Add(new ProductImage
                    {
                        ProductId = p.Id,
                        Url = boxImages[(imgIdx + 1) % boxImages.Length],
                        AltText = $"{p.Name} Contents",
                        SortOrder = 1,
                        IsPrimary = false
                    });
                    imgIdx++;

                    context.Products.Add(p);


                }

                await context.SaveChangesAsync();

                // D7. Seed Demo Sample Orders matching ERP mockup
                if (customerEntity != null)
                {
                    var megaBox = products.First(p => p.SKU == "GB-MGA-002");
                    var flowerPot = products.First(p => p.SKU == "POT-BIG-001");
                    var sparkler = products.First(p => p.SKU == "SPK-10P-001");

                    var sampleOrders = new[]
                    {
                        new { OrderNum = "ORD-2026-001248", CustomerName = "Ramesh Kumar", Amount = 2499m, Status = OrderStatus.Confirmed, Payment = PaymentStatus.Paid, Method = PaymentMethod.UPI, Date = DateTime.UtcNow.AddMinutes(-30) },
                        new { OrderNum = "ORD-2026-001247", CustomerName = "Suresh Babu", Amount = 1999m, Status = OrderStatus.Processing, Payment = PaymentStatus.Paid, Method = PaymentMethod.CreditCard, Date = DateTime.UtcNow.AddHours(-2) },
                        new { OrderNum = "ORD-2026-001246", CustomerName = "Vijay Kumar", Amount = 3499m, Status = OrderStatus.Pending, Payment = PaymentStatus.Pending, Method = PaymentMethod.UPI, Date = DateTime.UtcNow.AddHours(-5) },
                        new { OrderNum = "ORD-2026-001245", CustomerName = "Arun Prasad", Amount = 749m, Status = OrderStatus.Delivered, Payment = PaymentStatus.Paid, Method = PaymentMethod.UPI, Date = DateTime.UtcNow.AddDays(-1) },
                        new { OrderNum = "ORD-2026-001244", CustomerName = "Karthik R", Amount = 5999m, Status = OrderStatus.Shipped, Payment = PaymentStatus.Paid, Method = PaymentMethod.NetBanking, Date = DateTime.UtcNow.AddDays(-1) }
                    };

                    foreach (var s in sampleOrders)
                    {
                        // Only a dispatched consignment has an LR/waybill and a carrier. Demo rows
                        // that are still Pending/Confirmed/Processing must carry neither, exactly
                        // like a real order before the dispatch desk touches it.
                        var isDispatched = s.Status is OrderStatus.Shipped or OrderStatus.OutForDelivery or OrderStatus.Delivered;

                        var order = new Order
                        {
                            OrderNumber = s.OrderNum,
                            CustomerId = customerEntity.Id,
                            PaymentMethod = s.Method,
                            PaymentStatus = s.Payment,
                            OrderStatus = s.Status,
                            ItemsSubtotal = Money.FromDecimal(s.Amount * 0.82m),
                            Tax = Money.FromDecimal(s.Amount * 0.18m),
                            ShippingCharge = Money.Zero(),
                            Discount = Money.Zero(),
                            GrandTotal = Money.FromDecimal(s.Amount),
                            PlacedAtUtc = s.Date,
                            CarrierName = isDispatched ? "Sri Lakshmi Transports" : null,
                            TrackingNumber = isDispatched ? $"LR-{Random.Shared.Next(10000000, 99999999)}" : null,
                            ShippingAddress = new Address(
                                s.CustomerName,
                                "9876543210",
                                "123, West Street, Sivanandapuram",
                                "",
                                "Coimbatore",
                                "Tamil Nadu",
                                "641012",
                                "India")
                        };

                        order.Items.Add(new OrderItem
                        {
                            OrderId = order.Id,
                            ProductId = megaBox.Id,
                            ProductNameSnapshot = megaBox.Name,
                            SKUSnapshot = megaBox.SKU,
                            UnitPrice = megaBox.Price,
                            Quantity = 1,
                            LineTotal = megaBox.Price
                        });

                        order.StatusHistories.Add(new OrderStatusHistory
                        {
                            OrderId = order.Id,
                            FromStatus = OrderStatus.Pending,
                            ToStatus = s.Status,
                            Reason = "Order processed through system",
                            ChangedBy = "System",
                            ChangedAtUtc = s.Date
                        });

                        var invoice = new Invoice
                        {
                            InvoiceNumber = $"INV-{order.OrderNumber.Replace("ORD-", "")}",
                            OrderId = order.Id,
                            CustomerId = customerEntity.Id,
                            Subtotal = order.ItemsSubtotal,
                            Discount = order.Discount,
                            Tax = order.Tax,
                            Shipping = order.ShippingCharge,
                            GrandTotal = order.GrandTotal,
                            PaidAmount = s.Payment == PaymentStatus.Paid ? order.GrandTotal : Money.Zero(),
                            BalanceAmount = s.Payment == PaymentStatus.Paid ? Money.Zero() : order.GrandTotal,
                            Status = s.Payment == PaymentStatus.Paid ? InvoiceStatus.Paid : InvoiceStatus.Issued,
                            IssuedAtUtc = s.Date,
                            DueDateUtc = s.Date.AddDays(7)
                        };
                        order.Invoices.Add(invoice);

                        if (s.Payment == PaymentStatus.Paid)
                        {
                            order.Payments.Add(new Payment
                            {
                                PaymentNumber = $"PAY-{order.OrderNumber.Replace("ORD-", "")}",
                                OrderId = order.Id,
                                CustomerId = customerEntity.Id,
                                Amount = order.GrandTotal,
                                PaymentMethod = s.Method,
                                PaymentStatus = PaymentStatus.Paid,
                                TransactionReference = $"TXN-{Random.Shared.Next(1000000, 9999999)}",
                                PaidAtUtc = s.Date
                            });
                        }

                        context.Orders.Add(order);
                    }

                    await context.SaveChangesAsync();
                }

                // D8. Seed Demo Promotions
                context.Promotions.AddRange(
                    new Promotion
                    {
                        Code = "DIWALI2026",
                        Name = "Diwali Festive Bonanza",
                        Description = "15% off on all orders above ₹2,500",
                        DiscountType = DiscountType.Percentage,
                        DiscountValue = 15m,
                        MinimumOrderAmount = Money.FromDecimal(2500m),
                        MaximumDiscount = Money.FromDecimal(1000m),
                        StartDateUtc = DateTime.UtcNow.AddDays(-30),
                        EndDateUtc = DateTime.UtcNow.AddDays(90),
                        IsActive = true
                    },
                    new Promotion
                    {
                        Code = "WELCOME10",
                        Name = "New Customer Discount",
                        Description = "Flat 10% off for first-time shoppers",
                        DiscountType = DiscountType.Percentage,
                        DiscountValue = 10m,
                        MinimumOrderAmount = Money.FromDecimal(1000m),
                        MaximumDiscount = Money.FromDecimal(500m),
                        IsActive = true
                    },
                    new Promotion
                    {
                        Code = "AADHI20",
                        Name = "Mega Gift Box Discount",
                        Description = "20% off on all gift boxes",
                        DiscountType = DiscountType.Percentage,
                        DiscountValue = 20m,
                        MinimumOrderAmount = Money.FromDecimal(3000m),
                        MaximumDiscount = Money.FromDecimal(1500m),
                        IsActive = true
                    }
                );

                await context.SaveChangesAsync();
            }
    }
}
