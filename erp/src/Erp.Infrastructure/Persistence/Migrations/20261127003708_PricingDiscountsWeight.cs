using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Erp.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PricingDiscountsWeight : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "DiscountPercent",
                table: "SalesOrderLines",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "DiscountAmount",
                table: "SalesInvoices",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "DiscountPercent",
                table: "SalesInvoiceLines",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "DistributorPrice",
                table: "Items",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "WeightKg",
                table: "Items",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddCheckConstraint(
                name: "CK_SalesOrderLine_Discount",
                table: "SalesOrderLines",
                sql: "[DiscountPercent] >= 0 AND [DiscountPercent] <= 100");

            migrationBuilder.AddCheckConstraint(
                name: "CK_SalesInvoiceLine_Discount",
                table: "SalesInvoiceLines",
                sql: "[DiscountPercent] >= 0 AND [DiscountPercent] <= 100");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_SalesOrderLine_Discount",
                table: "SalesOrderLines");

            migrationBuilder.DropCheckConstraint(
                name: "CK_SalesInvoiceLine_Discount",
                table: "SalesInvoiceLines");

            migrationBuilder.DropColumn(
                name: "DiscountPercent",
                table: "SalesOrderLines");

            migrationBuilder.DropColumn(
                name: "DiscountAmount",
                table: "SalesInvoices");

            migrationBuilder.DropColumn(
                name: "DiscountPercent",
                table: "SalesInvoiceLines");

            migrationBuilder.DropColumn(
                name: "DistributorPrice",
                table: "Items");

            migrationBuilder.DropColumn(
                name: "WeightKg",
                table: "Items");
        }
    }
}
