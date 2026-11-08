using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Oluso.EntityFramework.Migrations.SqlServer
{
    /// <inheritdoc />
    public partial class AddWhatsAppChannel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "WhatsAppAccounts",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    PhoneNumberId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    BusinessAccountId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    DisplayPhoneNumber = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    VerifiedName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    QualityRating = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    LastVerifiedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    TenantId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WhatsAppAccounts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WhatsAppContacts",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    PhoneNumber = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ConsentStatus = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    ConsentSource = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    ConsentAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    OptedOutAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastInboundAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastInboundMessageId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    LastOutboundAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    TenantId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WhatsAppContacts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WhatsAppInboundMessages",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ProviderMessageId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    ContactId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    From = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    MessageType = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Text = table.Column<string>(type: "nvarchar(max)", maxLength: 4096, nullable: true),
                    Keyword = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    InReplyToMessageId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    ReceivedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ProcessedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RawJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    TenantId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WhatsAppInboundMessages", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WhatsAppTemplates",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ProviderTemplateId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    Language = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Category = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    BodyText = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: true),
                    BodyParameterCount = table.Column<int>(type: "int", nullable: false),
                    HeaderFormat = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true),
                    ComponentsJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SyncedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    TenantId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WhatsAppTemplates", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppAccounts_PhoneNumberId",
                table: "WhatsAppAccounts",
                column: "PhoneNumberId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppAccounts_TenantId",
                table: "WhatsAppAccounts",
                column: "TenantId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppContacts_TenantId_ConsentStatus",
                table: "WhatsAppContacts",
                columns: new[] { "TenantId", "ConsentStatus" });

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppContacts_TenantId_PhoneNumber",
                table: "WhatsAppContacts",
                columns: new[] { "TenantId", "PhoneNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppInboundMessages_TenantId_ContactId_ReceivedAt",
                table: "WhatsAppInboundMessages",
                columns: new[] { "TenantId", "ContactId", "ReceivedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppInboundMessages_TenantId_ProviderMessageId",
                table: "WhatsAppInboundMessages",
                columns: new[] { "TenantId", "ProviderMessageId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppTemplates_TenantId_Name_Language",
                table: "WhatsAppTemplates",
                columns: new[] { "TenantId", "Name", "Language" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WhatsAppAccounts");

            migrationBuilder.DropTable(
                name: "WhatsAppContacts");

            migrationBuilder.DropTable(
                name: "WhatsAppInboundMessages");

            migrationBuilder.DropTable(
                name: "WhatsAppTemplates");
        }
    }
}
