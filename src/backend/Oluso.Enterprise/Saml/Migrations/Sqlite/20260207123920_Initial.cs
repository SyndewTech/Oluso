using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Oluso.Enterprise.Saml.Migrations.Sqlite
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SamlServiceProviders",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    EntityId = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    DisplayName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    Description = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    Enabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    MetadataUrl = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    AssertionConsumerServiceUrl = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    SingleLogoutServiceUrl = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    SigningCertificate = table.Column<string>(type: "TEXT", nullable: true),
                    EncryptionCertificate = table.Column<string>(type: "TEXT", nullable: true),
                    EncryptAssertions = table.Column<bool>(type: "INTEGER", nullable: false),
                    NameIdFormat = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    AllowedClaimsJson = table.Column<string>(type: "TEXT", nullable: true),
                    ClaimMappingsJson = table.Column<string>(type: "TEXT", nullable: true),
                    SsoBinding = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    SignResponses = table.Column<bool>(type: "INTEGER", nullable: false),
                    SignAssertions = table.Column<bool>(type: "INTEGER", nullable: false),
                    RequireSignedAuthnRequests = table.Column<bool>(type: "INTEGER", nullable: false),
                    DefaultRelayState = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    PropertiesJson = table.Column<string>(type: "TEXT", nullable: true),
                    NonEditable = table.Column<bool>(type: "INTEGER", nullable: false),
                    Created = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Updated = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastAccessed = table.Column<DateTime>(type: "TEXT", nullable: true),
                    TenantId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SamlServiceProviders", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SamlServiceProviders_TenantId",
                table: "SamlServiceProviders",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_SamlServiceProviders_TenantId_EntityId",
                table: "SamlServiceProviders",
                columns: new[] { "TenantId", "EntityId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SamlServiceProviders");
        }
    }
}
