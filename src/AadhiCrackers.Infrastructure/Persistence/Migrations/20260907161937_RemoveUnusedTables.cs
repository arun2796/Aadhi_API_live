using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AadhiCrackers.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RemoveUnusedTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // IF EXISTS: legacy SQLite databases that were baselined from an EnsureCreated schema
            // may not contain some of these tables — the drop must be idempotent either way.
            migrationBuilder.Sql("""DROP TABLE IF EXISTS "GiftBoxItems";""");
            migrationBuilder.Sql("""DROP TABLE IF EXISTS "ProductVariants";""");
            migrationBuilder.Sql("""DROP TABLE IF EXISTS "QuoteItems";""");
            migrationBuilder.Sql("""DROP TABLE IF EXISTS "Quotes";""");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "GiftBoxItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ComponentProductId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ParentProductId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", nullable: true),
                    IsDeleted = table.Column<bool>(type: "INTEGER", nullable: false),
                    Quantity = table.Column<int>(type: "INTEGER", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    UpdatedBy = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GiftBoxItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GiftBoxItems_Products_ComponentProductId",
                        column: x => x.ComponentProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_GiftBoxItems_Products_ParentProductId",
                        column: x => x.ParentProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ProductVariants",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProductId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CostPrice = table.Column<long>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", nullable: true),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsDeleted = table.Column<bool>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 150, nullable: false),
                    Price = table.Column<long>(type: "INTEGER", nullable: false),
                    SKU = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    StockQuantity = table.Column<int>(type: "INTEGER", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    UpdatedBy = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductVariants", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProductVariants_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Quotes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CustomerId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ConvertedOrderId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", nullable: true),
                    Discount = table.Column<long>(type: "INTEGER", precision: 18, scale: 2, nullable: false),
                    ExpiryDateUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    GrandTotal = table.Column<long>(type: "INTEGER", precision: 18, scale: 2, nullable: false),
                    IsDeleted = table.Column<bool>(type: "INTEGER", nullable: false),
                    Notes = table.Column<string>(type: "TEXT", nullable: true),
                    QuoteNumber = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    Subtotal = table.Column<long>(type: "INTEGER", precision: 18, scale: 2, nullable: false),
                    Tax = table.Column<long>(type: "INTEGER", precision: 18, scale: 2, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    UpdatedBy = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Quotes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Quotes_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "QuoteItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProductId = table.Column<Guid>(type: "TEXT", nullable: false),
                    QuoteId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", nullable: true),
                    DiscountPercentage = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    IsDeleted = table.Column<bool>(type: "INTEGER", nullable: false),
                    LineTotal = table.Column<long>(type: "INTEGER", precision: 18, scale: 2, nullable: false),
                    Quantity = table.Column<int>(type: "INTEGER", nullable: false),
                    UnitPrice = table.Column<long>(type: "INTEGER", precision: 18, scale: 2, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    UpdatedBy = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QuoteItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_QuoteItems_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_QuoteItems_Quotes_QuoteId",
                        column: x => x.QuoteId,
                        principalTable: "Quotes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_GiftBoxItems_ComponentProductId",
                table: "GiftBoxItems",
                column: "ComponentProductId");

            migrationBuilder.CreateIndex(
                name: "IX_GiftBoxItems_ParentProductId_ComponentProductId",
                table: "GiftBoxItems",
                columns: new[] { "ParentProductId", "ComponentProductId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProductVariants_ProductId",
                table: "ProductVariants",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductVariants_SKU",
                table: "ProductVariants",
                column: "SKU",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_QuoteItems_ProductId",
                table: "QuoteItems",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_QuoteItems_QuoteId",
                table: "QuoteItems",
                column: "QuoteId");

            migrationBuilder.CreateIndex(
                name: "IX_Quotes_CustomerId",
                table: "Quotes",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_Quotes_QuoteNumber",
                table: "Quotes",
                column: "QuoteNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Quotes_Status",
                table: "Quotes",
                column: "Status");
        }
    }
}
