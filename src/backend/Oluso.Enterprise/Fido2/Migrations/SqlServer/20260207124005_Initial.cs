using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Oluso.Enterprise.Fido2.Migrations.SqlServer
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Fido2Credentials",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    UserId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    CredentialId = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: false),
                    PublicKey = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    UserHandle = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    SignatureCounter = table.Column<long>(type: "bigint", nullable: false),
                    CredentialType = table.Column<int>(type: "int", nullable: false),
                    AaGuid = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    AuthenticatorType = table.Column<int>(type: "int", nullable: false),
                    AttestationFormat = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    IsDiscoverable = table.Column<bool>(type: "bit", nullable: false),
                    Transports = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastUsedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    TenantId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Fido2Credentials", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Fido2Credentials_TenantId_CredentialId",
                table: "Fido2Credentials",
                columns: new[] { "TenantId", "CredentialId" },
                unique: true,
                filter: "[TenantId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Fido2Credentials_TenantId_UserId",
                table: "Fido2Credentials",
                columns: new[] { "TenantId", "UserId" });

            migrationBuilder.CreateIndex(
                name: "IX_Fido2Credentials_TenantId_UserId_IsActive",
                table: "Fido2Credentials",
                columns: new[] { "TenantId", "UserId", "IsActive" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Fido2Credentials");
        }
    }
}
