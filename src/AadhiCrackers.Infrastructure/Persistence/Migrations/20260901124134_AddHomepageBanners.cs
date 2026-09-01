using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AadhiCrackers.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddHomepageBanners : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "OrderId",
                table: "ProductReviews",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "OrderItemId",
                table: "ProductReviews",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Title",
                table: "ProductReviews",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "HomepageBanners",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 150, nullable: false),
                    Subtitle = table.Column<string>(type: "TEXT", nullable: true),
                    ImageUrl = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    MobileImageUrl = table.Column<string>(type: "TEXT", nullable: true),
                    TargetUrl = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    CtaText = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    DisplayOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    StartDateUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    EndDateUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedBy = table.Column<string>(type: "TEXT", nullable: true),
                    UpdatedBy = table.Column<string>(type: "TEXT", nullable: true),
                    IsDeleted = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HomepageBanners", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_HomepageBanners_DisplayOrder",
                table: "HomepageBanners",
                column: "DisplayOrder");

            migrationBuilder.CreateIndex(
                name: "IX_HomepageBanners_IsActive",
                table: "HomepageBanners",
                column: "IsActive");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "HomepageBanners");

            migrationBuilder.DropColumn(
                name: "OrderId",
                table: "ProductReviews");

            migrationBuilder.DropColumn(
                name: "OrderItemId",
                table: "ProductReviews");

            migrationBuilder.DropColumn(
                name: "Title",
                table: "ProductReviews");
        }
    }
}
