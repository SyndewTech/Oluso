using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Oluso.Enterprise.Verification.Migrations.SqlServer
{
    /// <inheritdoc />
    public partial class BusinessVerification : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "Kyb");

            migrationBuilder.CreateTable(
                name: "BusinessChecks",
                schema: "Kyb",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    BusinessId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    ApplicantId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    Country = table.Column<string>(type: "nvarchar(2)", maxLength: 2, nullable: false),
                    RegistrationNumber = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                    ProviderId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    Environment = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true),
                    Outcome = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    SessionReference = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    JourneyId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    StepId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    ProviderReference = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    Message = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ReusedFromCheckId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    OriginKind = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    OriginId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    OriginName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    TenantId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BusinessChecks", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Businesses",
                schema: "Kyb",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Country = table.Column<string>(type: "nvarchar(2)", maxLength: 2, nullable: false),
                    RegistrationNumber = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    RegistrationType = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true),
                    RegisteredName = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    EntityType = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    RegistrationDate = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    RegisteredAddress = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ActivityStatus = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    RawStatus = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    PeopleJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RegistryCheckedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RegistryCheckId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    RegistryProviderId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    RegistryEnvironment = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                    StatusReason = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    StatusMessage = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    LatestCheckId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DecidedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ReviewedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    ReviewedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ReviewNote = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    TenantId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Businesses", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "BusinessApplicants",
                schema: "Kyb",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    BusinessId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    SubjectId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    JourneyId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    StepId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    Relationship = table.Column<string>(type: "nvarchar(48)", maxLength: 48, nullable: false),
                    EvidenceRequirement = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    ProvidedBusinessName = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    NameMatched = table.Column<bool>(type: "bit", nullable: true),
                    DirectorMatched = table.Column<bool>(type: "bit", nullable: true),
                    MatchedPersonName = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    MatchedPersonRole = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    DocumentIdsJson = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                    StatusReason = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    StatusMessage = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    NotifyEmail = table.Column<string>(type: "nvarchar(320)", maxLength: 320, nullable: true),
                    ResumeUrl = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DecidedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ReviewedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    ReviewedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ReviewNote = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    TenantId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BusinessApplicants", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BusinessApplicants_Businesses_BusinessId",
                        column: x => x.BusinessId,
                        principalSchema: "Kyb",
                        principalTable: "Businesses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Kyb_BusinessApplicants_BusinessId",
                schema: "Kyb",
                table: "BusinessApplicants",
                column: "BusinessId");

            migrationBuilder.CreateIndex(
                name: "IX_Kyb_BusinessApplicants_TenantId_BusinessId_SubjectId",
                schema: "Kyb",
                table: "BusinessApplicants",
                columns: new[] { "TenantId", "BusinessId", "SubjectId" });

            migrationBuilder.CreateIndex(
                name: "IX_Kyb_BusinessApplicants_TenantId_JourneyId",
                schema: "Kyb",
                table: "BusinessApplicants",
                columns: new[] { "TenantId", "JourneyId" });

            migrationBuilder.CreateIndex(
                name: "IX_Kyb_BusinessApplicants_TenantId_Status_UpdatedAt",
                schema: "Kyb",
                table: "BusinessApplicants",
                columns: new[] { "TenantId", "Status", "UpdatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Kyb_BusinessApplicants_TenantId_SubjectId",
                schema: "Kyb",
                table: "BusinessApplicants",
                columns: new[] { "TenantId", "SubjectId" });

            migrationBuilder.CreateIndex(
                name: "IX_Kyb_BusinessChecks_TenantId_BusinessId_CreatedAt",
                schema: "Kyb",
                table: "BusinessChecks",
                columns: new[] { "TenantId", "BusinessId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Kyb_BusinessChecks_TenantId_ProviderId_SessionReference",
                schema: "Kyb",
                table: "BusinessChecks",
                columns: new[] { "TenantId", "ProviderId", "SessionReference" });

            migrationBuilder.CreateIndex(
                name: "IX_Kyb_Businesses_TenantId_Country_RegistrationNumber",
                schema: "Kyb",
                table: "Businesses",
                columns: new[] { "TenantId", "Country", "RegistrationNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Kyb_Businesses_TenantId_Status_UpdatedAt",
                schema: "Kyb",
                table: "Businesses",
                columns: new[] { "TenantId", "Status", "UpdatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BusinessApplicants",
                schema: "Kyb");

            migrationBuilder.DropTable(
                name: "BusinessChecks",
                schema: "Kyb");

            migrationBuilder.DropTable(
                name: "Businesses",
                schema: "Kyb");
        }
    }
}
