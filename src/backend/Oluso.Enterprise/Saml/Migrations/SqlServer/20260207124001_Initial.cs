using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Oluso.Enterprise.Saml.Migrations.SqlServer
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
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EntityId = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Enabled = table.Column<bool>(type: "bit", nullable: false),
                    MetadataUrl = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    AssertionConsumerServiceUrl = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    SingleLogoutServiceUrl = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    SigningCertificate = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    EncryptionCertificate = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    EncryptAssertions = table.Column<bool>(type: "bit", nullable: false),
                    NameIdFormat = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    AllowedClaimsJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ClaimMappingsJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SsoBinding = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    SignResponses = table.Column<bool>(type: "bit", nullable: false),
                    SignAssertions = table.Column<bool>(type: "bit", nullable: false),
                    RequireSignedAuthnRequests = table.Column<bool>(type: "bit", nullable: false),
                    DefaultRelayState = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    PropertiesJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    NonEditable = table.Column<bool>(type: "bit", nullable: false),
                    Created = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Updated = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastAccessed = table.Column<DateTime>(type: "datetime2", nullable: true),
                    TenantId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false)
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
