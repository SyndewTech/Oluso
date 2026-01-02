using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Oluso.Enterprise.Scim.Migrations.Sqlite
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ScimAttributeMappings",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    TenantId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    ScimClientId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    ScimAttribute = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    InternalProperty = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Direction = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    IsRequired = table.Column<bool>(type: "INTEGER", nullable: false),
                    DefaultValue = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    Transformation = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    Priority = table.Column<int>(type: "INTEGER", nullable: false),
                    IsEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScimAttributeMappings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ScimClients",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    TenantId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    IsEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    TokenHash = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    TokenCreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    TokenExpiresAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    AllowedIpRanges = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    RateLimitPerMinute = table.Column<int>(type: "INTEGER", nullable: false),
                    CanCreateUsers = table.Column<bool>(type: "INTEGER", nullable: false),
                    CanUpdateUsers = table.Column<bool>(type: "INTEGER", nullable: false),
                    CanDeleteUsers = table.Column<bool>(type: "INTEGER", nullable: false),
                    CanManageGroups = table.Column<bool>(type: "INTEGER", nullable: false),
                    AttributeMappings = table.Column<string>(type: "TEXT", nullable: true),
                    DefaultRoleId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastActivityAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    SuccessCount = table.Column<long>(type: "INTEGER", nullable: false),
                    ErrorCount = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScimClients", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ScimProvisioningLogs",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    TenantId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    ScimClientId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Method = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    Path = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    ResourceType = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    ResourceId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    Operation = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    StatusCode = table.Column<int>(type: "INTEGER", nullable: false),
                    Success = table.Column<bool>(type: "INTEGER", nullable: false),
                    ErrorMessage = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    RequestBody = table.Column<string>(type: "TEXT", nullable: true),
                    ResponseBody = table.Column<string>(type: "TEXT", nullable: true),
                    ClientIp = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    DurationMs = table.Column<long>(type: "INTEGER", nullable: false),
                    Timestamp = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScimProvisioningLogs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ScimResourceMappings",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    TenantId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    ScimClientId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    ResourceType = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    ExternalId = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    InternalId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScimResourceMappings", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ScimAttributeMappings_ScimClientId",
                table: "ScimAttributeMappings",
                column: "ScimClientId");

            migrationBuilder.CreateIndex(
                name: "IX_ScimAttributeMappings_ScimClientId_ScimAttribute",
                table: "ScimAttributeMappings",
                columns: new[] { "ScimClientId", "ScimAttribute" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ScimAttributeMappings_TenantId",
                table: "ScimAttributeMappings",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_ScimClients_TenantId",
                table: "ScimClients",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_ScimClients_TenantId_Name",
                table: "ScimClients",
                columns: new[] { "TenantId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ScimClients_TokenHash",
                table: "ScimClients",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ScimProvisioningLogs_ScimClientId",
                table: "ScimProvisioningLogs",
                column: "ScimClientId");

            migrationBuilder.CreateIndex(
                name: "IX_ScimProvisioningLogs_ScimClientId_Timestamp",
                table: "ScimProvisioningLogs",
                columns: new[] { "ScimClientId", "Timestamp" });

            migrationBuilder.CreateIndex(
                name: "IX_ScimProvisioningLogs_TenantId",
                table: "ScimProvisioningLogs",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_ScimProvisioningLogs_Timestamp",
                table: "ScimProvisioningLogs",
                column: "Timestamp");

            migrationBuilder.CreateIndex(
                name: "IX_ScimResourceMappings_ScimClientId_ResourceType_ExternalId",
                table: "ScimResourceMappings",
                columns: new[] { "ScimClientId", "ResourceType", "ExternalId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ScimResourceMappings_ScimClientId_ResourceType_InternalId",
                table: "ScimResourceMappings",
                columns: new[] { "ScimClientId", "ResourceType", "InternalId" });

            migrationBuilder.CreateIndex(
                name: "IX_ScimResourceMappings_TenantId",
                table: "ScimResourceMappings",
                column: "TenantId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ScimAttributeMappings");

            migrationBuilder.DropTable(
                name: "ScimClients");

            migrationBuilder.DropTable(
                name: "ScimProvisioningLogs");

            migrationBuilder.DropTable(
                name: "ScimResourceMappings");
        }
    }
}
