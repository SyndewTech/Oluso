using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Oluso.Enterprise.Scim.Migrations.Sqlite
{
    /// <inheritdoc />
    public partial class AddScimProviders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ScimProviderAttributeMappings",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    ScimProviderId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    ScimAttribute = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    InternalProperty = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Expression = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    Condition = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    Transformation = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    DefaultValue = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    IsEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    TenantId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScimProviderAttributeMappings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ScimProviderLogs",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    ScimProviderId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Operation = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    ResourceType = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    InternalId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    RemoteId = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    Method = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    Path = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    StatusCode = table.Column<int>(type: "INTEGER", nullable: false),
                    Success = table.Column<bool>(type: "INTEGER", nullable: false),
                    ErrorMessage = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    RequestBody = table.Column<string>(type: "TEXT", nullable: true),
                    ResponseBody = table.Column<string>(type: "TEXT", nullable: true),
                    DurationMs = table.Column<long>(type: "INTEGER", nullable: false),
                    Timestamp = table.Column<DateTime>(type: "TEXT", nullable: false),
                    TenantId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScimProviderLogs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ScimProviderOutbox",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    ScimProviderId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    ResourceType = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    InternalId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Operation = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Priority = table.Column<int>(type: "INTEGER", nullable: false),
                    Attempts = table.Column<int>(type: "INTEGER", nullable: false),
                    NextAttemptAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LockedUntil = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LockedBy = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    LastStatusCode = table.Column<int>(type: "INTEGER", nullable: true),
                    LastError = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    Reason = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    TenantId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScimProviderOutbox", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ScimProviderResourceMappings",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    ScimProviderId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    ResourceType = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    InternalId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    RemoteId = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    RemoteVersion = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    PayloadHash = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    IsDeprovisioned = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastSyncedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    TenantId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScimProviderResourceMappings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ScimProviders",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    IsEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    Flavor = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    BaseUrl = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    AuthType = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    OAuthTokenEndpoint = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    OAuthClientId = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    OAuthScope = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    SecretUpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    SyncUsers = table.Column<bool>(type: "INTEGER", nullable: false),
                    SyncGroups = table.Column<bool>(type: "INTEGER", nullable: false),
                    GroupSource = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    ScopeRoleIds = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: true),
                    ScopeTeamIds = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: true),
                    UserNameSource = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    UserNameExpression = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    DeprovisionMode = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    ReconcileIntervalMinutes = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastReconcileAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastReconcileSummary = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    LastSuccessAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastErrorAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastError = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    SuccessCount = table.Column<long>(type: "INTEGER", nullable: false),
                    ErrorCount = table.Column<long>(type: "INTEGER", nullable: false),
                    TenantId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScimProviders", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ScimProviderAttributeMappings_ScimProviderId_ScimAttribute",
                table: "ScimProviderAttributeMappings",
                columns: new[] { "ScimProviderId", "ScimAttribute" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ScimProviderAttributeMappings_TenantId",
                table: "ScimProviderAttributeMappings",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_ScimProviderLogs_ScimProviderId_Timestamp",
                table: "ScimProviderLogs",
                columns: new[] { "ScimProviderId", "Timestamp" });

            migrationBuilder.CreateIndex(
                name: "IX_ScimProviderLogs_TenantId",
                table: "ScimProviderLogs",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_ScimProviderLogs_Timestamp",
                table: "ScimProviderLogs",
                column: "Timestamp");

            migrationBuilder.CreateIndex(
                name: "IX_ScimProviderOutbox_ScimProviderId_ResourceType_InternalId",
                table: "ScimProviderOutbox",
                columns: new[] { "ScimProviderId", "ResourceType", "InternalId" });

            migrationBuilder.CreateIndex(
                name: "IX_ScimProviderOutbox_ScimProviderId_Status_Priority_CreatedAt",
                table: "ScimProviderOutbox",
                columns: new[] { "ScimProviderId", "Status", "Priority", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ScimProviderOutbox_Status_NextAttemptAt",
                table: "ScimProviderOutbox",
                columns: new[] { "Status", "NextAttemptAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ScimProviderOutbox_TenantId",
                table: "ScimProviderOutbox",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_ScimProviderResourceMappings_ScimProviderId_ResourceType_InternalId",
                table: "ScimProviderResourceMappings",
                columns: new[] { "ScimProviderId", "ResourceType", "InternalId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ScimProviderResourceMappings_ScimProviderId_ResourceType_RemoteId",
                table: "ScimProviderResourceMappings",
                columns: new[] { "ScimProviderId", "ResourceType", "RemoteId" });

            migrationBuilder.CreateIndex(
                name: "IX_ScimProviderResourceMappings_TenantId",
                table: "ScimProviderResourceMappings",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_ScimProviders_TenantId",
                table: "ScimProviders",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_ScimProviders_TenantId_Name",
                table: "ScimProviders",
                columns: new[] { "TenantId", "Name" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ScimProviderAttributeMappings");

            migrationBuilder.DropTable(
                name: "ScimProviderLogs");

            migrationBuilder.DropTable(
                name: "ScimProviderOutbox");

            migrationBuilder.DropTable(
                name: "ScimProviderResourceMappings");

            migrationBuilder.DropTable(
                name: "ScimProviders");
        }
    }
}
