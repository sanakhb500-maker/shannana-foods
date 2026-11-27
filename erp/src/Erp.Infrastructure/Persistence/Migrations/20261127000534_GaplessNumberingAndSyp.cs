using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Erp.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class GaplessNumberingAndSyp : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Number",
                table: "JournalEntries",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "DocumentSequences",
                columns: table => new
                {
                    Key = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    LastNumber = table.Column<int>(type: "int", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentSequences", x => x.Key);
                });

            migrationBuilder.CreateIndex(
                name: "IX_JournalEntries_Number",
                table: "JournalEntries",
                column: "Number",
                unique: true,
                filter: "[Number] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DocumentSequences");

            migrationBuilder.DropIndex(
                name: "IX_JournalEntries_Number",
                table: "JournalEntries");

            migrationBuilder.DropColumn(
                name: "Number",
                table: "JournalEntries");
        }
    }
}
