using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Oluso.EntityFramework.Migrations.Sqlite
{
    /// <inheritdoc />
    public partial class AddFileAttachmentProtection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "EntityIdHash",
                table: "FileAttachments",
                type: "TEXT",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EntityIdProtected",
                table: "FileAttachments",
                type: "TEXT",
                maxLength: 4000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FileNameProtected",
                table: "FileAttachments",
                type: "TEXT",
                maxLength: 4000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProtectionPurpose",
                table: "FileAttachments",
                type: "TEXT",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SubEntityIdHash",
                table: "FileAttachments",
                type: "TEXT",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SubEntityIdProtected",
                table: "FileAttachments",
                type: "TEXT",
                maxLength: 4000,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_FileAttachments_TenantId_EntityType_EntityIdHash",
                table: "FileAttachments",
                columns: new[] { "TenantId", "EntityType", "EntityIdHash" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_FileAttachments_TenantId_EntityType_EntityIdHash",
                table: "FileAttachments");

            migrationBuilder.DropColumn(
                name: "EntityIdHash",
                table: "FileAttachments");

            migrationBuilder.DropColumn(
                name: "EntityIdProtected",
                table: "FileAttachments");

            migrationBuilder.DropColumn(
                name: "FileNameProtected",
                table: "FileAttachments");

            migrationBuilder.DropColumn(
                name: "ProtectionPurpose",
                table: "FileAttachments");

            migrationBuilder.DropColumn(
                name: "SubEntityIdHash",
                table: "FileAttachments");

            migrationBuilder.DropColumn(
                name: "SubEntityIdProtected",
                table: "FileAttachments");
        }
    }
}
