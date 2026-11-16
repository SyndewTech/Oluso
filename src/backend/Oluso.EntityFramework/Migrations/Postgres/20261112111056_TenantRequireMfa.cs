using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Oluso.EntityFramework.Migrations.Postgres
{
    /// <inheritdoc />
    public partial class TenantRequireMfa : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "RequireMfa",
                table: "Tenants",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RequireMfa",
                table: "Tenants");
        }
    }
}
