using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Oluso.EntityFramework.Migrations.SqlServer
{
    /// <inheritdoc />
    public partial class DropTenantBrandingVisualColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Branding_BackgroundColor",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "Branding_CustomCss",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "Branding_FaviconUrl",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "Branding_LogoUrl",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "Branding_PrimaryColor",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "Branding_SecondaryColor",
                table: "Tenants");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Branding_BackgroundColor",
                table: "Tenants",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Branding_CustomCss",
                table: "Tenants",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Branding_FaviconUrl",
                table: "Tenants",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Branding_LogoUrl",
                table: "Tenants",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Branding_PrimaryColor",
                table: "Tenants",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Branding_SecondaryColor",
                table: "Tenants",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);
        }
    }
}
