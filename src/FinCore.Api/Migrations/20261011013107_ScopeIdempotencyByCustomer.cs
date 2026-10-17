using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinCore.Api.Migrations
{
    /// <inheritdoc />
    public partial class ScopeIdempotencyByCustomer : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_IdempotencyRecords_Key",
                table: "IdempotencyRecords");

            migrationBuilder.AddColumn<Guid>(
                name: "CustomerId",
                table: "IdempotencyRecords",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateIndex(
                name: "IX_IdempotencyRecords_CustomerId_Key",
                table: "IdempotencyRecords",
                columns: new[] { "CustomerId", "Key" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_IdempotencyRecords_CustomerId_Key",
                table: "IdempotencyRecords");

            migrationBuilder.DropColumn(
                name: "CustomerId",
                table: "IdempotencyRecords");

            migrationBuilder.CreateIndex(
                name: "IX_IdempotencyRecords_Key",
                table: "IdempotencyRecords",
                column: "Key",
                unique: true);
        }
    }
}
