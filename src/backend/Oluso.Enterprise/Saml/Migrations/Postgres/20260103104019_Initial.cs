using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Oluso.Enterprise.Saml.Migrations.Postgres
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
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    EntityId = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    MetadataUrl = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    AssertionConsumerServiceUrl = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    SingleLogoutServiceUrl = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    SigningCertificate = table.Column<string>(type: "text", nullable: true),
                    EncryptionCertificate = table.Column<string>(type: "text", nullable: true),
                    EncryptAssertions = table.Column<bool>(type: "boolean", nullable: false),
                    NameIdFormat = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    AllowedClaimsJson = table.Column<string>(type: "text", nullable: true),
                    ClaimMappingsJson = table.Column<string>(type: "text", nullable: true),
                    SsoBinding = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    SignResponses = table.Column<bool>(type: "boolean", nullable: false),
                    SignAssertions = table.Column<bool>(type: "boolean", nullable: false),
                    RequireSignedAuthnRequests = table.Column<bool>(type: "boolean", nullable: false),
                    DefaultRelayState = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    PropertiesJson = table.Column<string>(type: "text", nullable: true),
                    NonEditable = table.Column<bool>(type: "boolean", nullable: false),
                    Created = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Updated = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastAccessed = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    TenantId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false)
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
