using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Oluso.EntityFramework.Migrations.Sqlite
{
    /// <inheritdoc />
    public partial class VerifiedEmailDomainsAndClientEmailRequirement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "RequireVerifiedEmail",
                table: "Clients",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "TenantEmailDomains",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    TenantId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Domain = table.Column<string>(type: "TEXT", maxLength: 253, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    VerificationToken = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    TxtRecordName = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    TxtRecordValue = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    VerifiedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastCheckedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CheckAttempts = table.Column<int>(type: "INTEGER", nullable: false),
                    FailureReason = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    LastRevalidatedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    GracePeriodStartedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TenantEmailDomains", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TenantEmailDomains_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TenantEmailDomains_Domain_Status",
                table: "TenantEmailDomains",
                columns: new[] { "Domain", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_TenantEmailDomains_TenantId_Domain",
                table: "TenantEmailDomains",
                columns: new[] { "TenantId", "Domain" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TenantEmailDomains");

            migrationBuilder.DropColumn(
                name: "RequireVerifiedEmail",
                table: "Clients");
        }
    }
}
