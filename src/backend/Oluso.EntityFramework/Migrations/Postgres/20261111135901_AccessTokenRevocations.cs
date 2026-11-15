using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Oluso.EntityFramework.Migrations.Postgres
{
    /// <inheritdoc />
    public partial class AccessTokenRevocations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AccessTokenRevocations",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                    ClientId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    SessionId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    TokenId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    RevokedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    TenantId = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccessTokenRevocations", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AccessTokenRevocations_ClientId_SessionId",
                table: "AccessTokenRevocations",
                columns: new[] { "ClientId", "SessionId" });

            migrationBuilder.CreateIndex(
                name: "IX_AccessTokenRevocations_ClientId_TokenId",
                table: "AccessTokenRevocations",
                columns: new[] { "ClientId", "TokenId" });

            migrationBuilder.CreateIndex(
                name: "IX_AccessTokenRevocations_ExpiresAt",
                table: "AccessTokenRevocations",
                column: "ExpiresAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AccessTokenRevocations");
        }
    }
}
