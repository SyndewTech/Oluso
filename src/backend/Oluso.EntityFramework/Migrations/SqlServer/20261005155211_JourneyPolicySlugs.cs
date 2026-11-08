using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Oluso.EntityFramework.Migrations.SqlServer
{
    /// <inheritdoc />
    public partial class JourneyPolicySlugs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Slug",
                table: "JourneyPolicies",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_JourneyPolicies_TenantId_Slug",
                table: "JourneyPolicies",
                columns: new[] { "TenantId", "Slug" },
                unique: true,
                filter: "[TenantId] IS NOT NULL AND [Slug] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_JourneyPolicies_TenantId_Slug",
                table: "JourneyPolicies");

            migrationBuilder.DropColumn(
                name: "Slug",
                table: "JourneyPolicies");
        }
    }
}
