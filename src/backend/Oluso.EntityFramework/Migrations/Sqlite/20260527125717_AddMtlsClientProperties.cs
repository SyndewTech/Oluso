using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Oluso.EntityFramework.Migrations.Sqlite
{
    /// <inheritdoc />
    public partial class AddMtlsClientProperties : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "TlsClientAuthSanDns",
                table: "Clients",
                type: "TEXT",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TlsClientAuthSanEmail",
                table: "Clients",
                type: "TEXT",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TlsClientAuthSanIp",
                table: "Clients",
                type: "TEXT",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TlsClientAuthSanUri",
                table: "Clients",
                type: "TEXT",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TlsClientAuthSubjectDn",
                table: "Clients",
                type: "TEXT",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TokenEndpointAuthMethod",
                table: "Clients",
                type: "TEXT",
                maxLength: 100,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TlsClientAuthSanDns",
                table: "Clients");

            migrationBuilder.DropColumn(
                name: "TlsClientAuthSanEmail",
                table: "Clients");

            migrationBuilder.DropColumn(
                name: "TlsClientAuthSanIp",
                table: "Clients");

            migrationBuilder.DropColumn(
                name: "TlsClientAuthSanUri",
                table: "Clients");

            migrationBuilder.DropColumn(
                name: "TlsClientAuthSubjectDn",
                table: "Clients");

            migrationBuilder.DropColumn(
                name: "TokenEndpointAuthMethod",
                table: "Clients");
        }
    }
}
