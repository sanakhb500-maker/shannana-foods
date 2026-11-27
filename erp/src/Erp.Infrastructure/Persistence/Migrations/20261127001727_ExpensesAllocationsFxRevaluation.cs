using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Erp.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ExpensesAllocationsFxRevaluation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "LocalSettled",
                table: "SupplierPayments",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "LocalSettled",
                table: "CustomerReceipts",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CustomerReceiptAllocations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CustomerReceiptId = table.Column<int>(type: "int", nullable: false),
                    SalesInvoiceId = table.Column<int>(type: "int", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomerReceiptAllocations", x => x.Id);
                    table.CheckConstraint("CK_CustomerReceiptAllocation_Amount", "[Amount] > 0");
                    table.ForeignKey(
                        name: "FK_CustomerReceiptAllocations_CustomerReceipts_CustomerReceiptId",
                        column: x => x.CustomerReceiptId,
                        principalTable: "CustomerReceipts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CustomerReceiptAllocations_SalesInvoices_SalesInvoiceId",
                        column: x => x.SalesInvoiceId,
                        principalTable: "SalesInvoices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ExpenseInvoices",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Number = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    DueDate = table.Column<DateOnly>(type: "date", nullable: true),
                    SupplierId = table.Column<int>(type: "int", nullable: false),
                    SupplierInvoiceNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    CurrencyId = table.Column<int>(type: "int", nullable: false),
                    ExchangeRate = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    TotalAmount = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    ConfirmedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ConfirmedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExpenseInvoices", x => x.Id);
                    table.CheckConstraint("CK_ExpenseInvoice_Rate", "[ExchangeRate] > 0");
                    table.ForeignKey(
                        name: "FK_ExpenseInvoices_Currencies_CurrencyId",
                        column: x => x.CurrencyId,
                        principalTable: "Currencies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ExpenseInvoices_Suppliers_SupplierId",
                        column: x => x.SupplierId,
                        principalTable: "Suppliers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ExpenseVouchers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Number = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    CurrencyId = table.Column<int>(type: "int", nullable: false),
                    ExchangeRate = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    Method = table.Column<int>(type: "int", nullable: false),
                    Payee = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    TotalAmount = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    ConfirmedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ConfirmedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExpenseVouchers", x => x.Id);
                    table.CheckConstraint("CK_ExpenseVoucher_Rate", "[ExchangeRate] > 0");
                    table.ForeignKey(
                        name: "FK_ExpenseVouchers_Currencies_CurrencyId",
                        column: x => x.CurrencyId,
                        principalTable: "Currencies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RevaluationRuns",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AsOf = table.Column<DateOnly>(type: "date", nullable: false),
                    JournalEntryId = table.Column<int>(type: "int", nullable: true),
                    ReversalEntryId = table.Column<int>(type: "int", nullable: true),
                    NetResult = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RevaluationRuns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RevaluationRuns_JournalEntries_JournalEntryId",
                        column: x => x.JournalEntryId,
                        principalTable: "JournalEntries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RevaluationRuns_JournalEntries_ReversalEntryId",
                        column: x => x.ReversalEntryId,
                        principalTable: "JournalEntries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ExpenseInvoiceLines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ExpenseInvoiceId = table.Column<int>(type: "int", nullable: false),
                    AccountId = table.Column<int>(type: "int", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Amount = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExpenseInvoiceLines", x => x.Id);
                    table.CheckConstraint("CK_ExpenseInvoiceLine_Amount", "[Amount] > 0");
                    table.ForeignKey(
                        name: "FK_ExpenseInvoiceLines_Accounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ExpenseInvoiceLines_ExpenseInvoices_ExpenseInvoiceId",
                        column: x => x.ExpenseInvoiceId,
                        principalTable: "ExpenseInvoices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SupplierPaymentAllocations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SupplierPaymentId = table.Column<int>(type: "int", nullable: false),
                    PurchaseInvoiceId = table.Column<int>(type: "int", nullable: true),
                    ExpenseInvoiceId = table.Column<int>(type: "int", nullable: true),
                    Amount = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SupplierPaymentAllocations", x => x.Id);
                    table.CheckConstraint("CK_SupplierPaymentAllocation_One", "[Amount] > 0 AND (([PurchaseInvoiceId] IS NOT NULL AND [ExpenseInvoiceId] IS NULL) OR ([PurchaseInvoiceId] IS NULL AND [ExpenseInvoiceId] IS NOT NULL))");
                    table.ForeignKey(
                        name: "FK_SupplierPaymentAllocations_ExpenseInvoices_ExpenseInvoiceId",
                        column: x => x.ExpenseInvoiceId,
                        principalTable: "ExpenseInvoices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SupplierPaymentAllocations_PurchaseInvoices_PurchaseInvoiceId",
                        column: x => x.PurchaseInvoiceId,
                        principalTable: "PurchaseInvoices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SupplierPaymentAllocations_SupplierPayments_SupplierPaymentId",
                        column: x => x.SupplierPaymentId,
                        principalTable: "SupplierPayments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ExpenseVoucherLines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ExpenseVoucherId = table.Column<int>(type: "int", nullable: false),
                    AccountId = table.Column<int>(type: "int", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Amount = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExpenseVoucherLines", x => x.Id);
                    table.CheckConstraint("CK_ExpenseVoucherLine_Amount", "[Amount] > 0");
                    table.ForeignKey(
                        name: "FK_ExpenseVoucherLines_Accounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ExpenseVoucherLines_ExpenseVouchers_ExpenseVoucherId",
                        column: x => x.ExpenseVoucherId,
                        principalTable: "ExpenseVouchers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CustomerReceiptAllocations_CustomerReceiptId",
                table: "CustomerReceiptAllocations",
                column: "CustomerReceiptId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerReceiptAllocations_SalesInvoiceId",
                table: "CustomerReceiptAllocations",
                column: "SalesInvoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_ExpenseInvoiceLines_AccountId",
                table: "ExpenseInvoiceLines",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_ExpenseInvoiceLines_ExpenseInvoiceId",
                table: "ExpenseInvoiceLines",
                column: "ExpenseInvoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_ExpenseInvoices_CurrencyId",
                table: "ExpenseInvoices",
                column: "CurrencyId");

            migrationBuilder.CreateIndex(
                name: "IX_ExpenseInvoices_Number",
                table: "ExpenseInvoices",
                column: "Number",
                unique: true,
                filter: "[Number] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ExpenseInvoices_SupplierId_Date",
                table: "ExpenseInvoices",
                columns: new[] { "SupplierId", "Date" });

            migrationBuilder.CreateIndex(
                name: "IX_ExpenseVoucherLines_AccountId",
                table: "ExpenseVoucherLines",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_ExpenseVoucherLines_ExpenseVoucherId",
                table: "ExpenseVoucherLines",
                column: "ExpenseVoucherId");

            migrationBuilder.CreateIndex(
                name: "IX_ExpenseVouchers_CurrencyId",
                table: "ExpenseVouchers",
                column: "CurrencyId");

            migrationBuilder.CreateIndex(
                name: "IX_ExpenseVouchers_Number",
                table: "ExpenseVouchers",
                column: "Number",
                unique: true,
                filter: "[Number] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_RevaluationRuns_AsOf",
                table: "RevaluationRuns",
                column: "AsOf",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RevaluationRuns_JournalEntryId",
                table: "RevaluationRuns",
                column: "JournalEntryId");

            migrationBuilder.CreateIndex(
                name: "IX_RevaluationRuns_ReversalEntryId",
                table: "RevaluationRuns",
                column: "ReversalEntryId");

            migrationBuilder.CreateIndex(
                name: "IX_SupplierPaymentAllocations_ExpenseInvoiceId",
                table: "SupplierPaymentAllocations",
                column: "ExpenseInvoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_SupplierPaymentAllocations_PurchaseInvoiceId",
                table: "SupplierPaymentAllocations",
                column: "PurchaseInvoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_SupplierPaymentAllocations_SupplierPaymentId",
                table: "SupplierPaymentAllocations",
                column: "SupplierPaymentId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CustomerReceiptAllocations");

            migrationBuilder.DropTable(
                name: "ExpenseInvoiceLines");

            migrationBuilder.DropTable(
                name: "ExpenseVoucherLines");

            migrationBuilder.DropTable(
                name: "RevaluationRuns");

            migrationBuilder.DropTable(
                name: "SupplierPaymentAllocations");

            migrationBuilder.DropTable(
                name: "ExpenseVouchers");

            migrationBuilder.DropTable(
                name: "ExpenseInvoices");

            migrationBuilder.DropColumn(
                name: "LocalSettled",
                table: "SupplierPayments");

            migrationBuilder.DropColumn(
                name: "LocalSettled",
                table: "CustomerReceipts");
        }
    }
}
