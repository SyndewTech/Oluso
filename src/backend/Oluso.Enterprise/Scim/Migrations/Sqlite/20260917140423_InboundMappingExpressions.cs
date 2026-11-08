using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Oluso.Enterprise.Scim.Migrations.Sqlite
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
                type: "TEXT",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Expression",
                table: "ScimAttributeMappings",
                type: "TEXT",
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
                type: "TEXT",
                maxLength: 20,
                nullable: false,
                defaultValue: "");
        }
    }
}
