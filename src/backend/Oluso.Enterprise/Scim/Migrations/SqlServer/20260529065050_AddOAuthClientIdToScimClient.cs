using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Oluso.Enterprise.Scim.Migrations.SqlServer
{
    /// <inheritdoc />
    public partial class AddOAuthClientIdToScimClient : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "OAuthClientId",
                table: "ScimClients",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ScimClients_TenantId_OAuthClientId",
                table: "ScimClients",
                columns: new[] { "TenantId", "OAuthClientId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ScimClients_TenantId_OAuthClientId",
                table: "ScimClients");

            migrationBuilder.DropColumn(
                name: "OAuthClientId",
                table: "ScimClients");
        }
    }
}
