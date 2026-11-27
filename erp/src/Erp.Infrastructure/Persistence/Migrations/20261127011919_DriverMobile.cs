using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Erp.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DriverMobile : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ActionAt",
                table: "TripStops",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "Latitude",
                table: "TripStops",
                type: "float",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "Longitude",
                table: "TripStops",
                type: "float",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "UserId",
                table: "Drivers",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TripStopId",
                table: "CustomerReceipts",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "StopProofs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TripStopId = table.Column<int>(type: "int", nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ContentType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StopProofs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StopProofs_TripStops_TripStopId",
                        column: x => x.TripStopId,
                        principalTable: "TripStops",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Drivers_UserId",
                table: "Drivers",
                column: "UserId",
                unique: true,
                filter: "[UserId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerReceipts_TripStopId",
                table: "CustomerReceipts",
                column: "TripStopId");

            migrationBuilder.CreateIndex(
                name: "IX_StopProofs_TripStopId",
                table: "StopProofs",
                column: "TripStopId");

            migrationBuilder.AddForeignKey(
                name: "FK_CustomerReceipts_TripStops_TripStopId",
                table: "CustomerReceipts",
                column: "TripStopId",
                principalTable: "TripStops",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Drivers_AspNetUsers_UserId",
                table: "Drivers",
                column: "UserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CustomerReceipts_TripStops_TripStopId",
                table: "CustomerReceipts");

            migrationBuilder.DropForeignKey(
                name: "FK_Drivers_AspNetUsers_UserId",
                table: "Drivers");

            migrationBuilder.DropTable(
                name: "StopProofs");

            migrationBuilder.DropIndex(
                name: "IX_Drivers_UserId",
                table: "Drivers");

            migrationBuilder.DropIndex(
                name: "IX_CustomerReceipts_TripStopId",
                table: "CustomerReceipts");

            migrationBuilder.DropColumn(
                name: "ActionAt",
                table: "TripStops");

            migrationBuilder.DropColumn(
                name: "Latitude",
                table: "TripStops");

            migrationBuilder.DropColumn(
                name: "Longitude",
                table: "TripStops");

            migrationBuilder.DropColumn(
                name: "UserId",
                table: "Drivers");

            migrationBuilder.DropColumn(
                name: "TripStopId",
                table: "CustomerReceipts");
        }
    }
}
