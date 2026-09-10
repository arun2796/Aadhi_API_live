using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AadhiCrackers.Infrastructure.MigrationsPostgres.Migrations
{
    /// <inheritdoc />
    public partial class AddOrderItemCompareAtPriceSnapshot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "CompareAtPriceSnapshot",
                table: "OrderItems",
                type: "bigint",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CompareAtPriceSnapshot",
                table: "OrderItems");
        }
    }
}
