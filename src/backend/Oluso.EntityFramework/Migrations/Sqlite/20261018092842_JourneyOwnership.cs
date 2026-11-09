using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Oluso.EntityFramework.Migrations.Sqlite
{
    /// <inheritdoc />
    public partial class JourneyOwnership : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "OwnerModule",
                table: "JourneyPolicies",
                type: "TEXT",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Sharing",
                table: "JourneyPolicies",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_JourneySubmissions_JourneyId",
                table: "JourneySubmissions",
                column: "JourneyId");

            migrationBuilder.CreateIndex(
                name: "IX_JourneyPolicies_TenantId_OwnerModule",
                table: "JourneyPolicies",
                columns: new[] { "TenantId", "OwnerModule" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_JourneySubmissions_JourneyId",
                table: "JourneySubmissions");

            migrationBuilder.DropIndex(
                name: "IX_JourneyPolicies_TenantId_OwnerModule",
                table: "JourneyPolicies");

            migrationBuilder.DropColumn(
                name: "OwnerModule",
                table: "JourneyPolicies");

            migrationBuilder.DropColumn(
                name: "Sharing",
                table: "JourneyPolicies");
        }
    }
}
