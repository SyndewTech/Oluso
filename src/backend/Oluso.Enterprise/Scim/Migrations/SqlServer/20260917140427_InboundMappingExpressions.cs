using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Oluso.Enterprise.Scim.Migrations.SqlServer
{
    /// <inheritdoc />
    public partial class InboundMappingExpressions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Direction",
                table: "ScimAttributeMappings");

            migrationBuilder.AddColumn<string>(
                name: "Condition",
                table: "ScimAttributeMappings",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Expression",
                table: "ScimAttributeMappings",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Condition",
                table: "ScimAttributeMappings");

            migrationBuilder.DropColumn(
                name: "Expression",
                table: "ScimAttributeMappings");

            migrationBuilder.AddColumn<string>(
                name: "Direction",
                table: "ScimAttributeMappings",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");
        }
    }
}
