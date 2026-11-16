using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Oluso.EntityFramework.Migrations.SqlServer
{
    /// <inheritdoc />
    public partial class ClientJwksUri : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "JwksUri",
                table: "Clients",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "JwksUri",
                table: "Clients");
        }
    }
}
