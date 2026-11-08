using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Oluso.Enterprise.Verification.Migrations.Sqlite
{
    /// <inheritdoc />
    public partial class BusinessVerification : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Kyb_BusinessChecks",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    BusinessId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    ApplicantId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    Country = table.Column<string>(type: "TEXT", maxLength: 2, nullable: false),
                    RegistrationNumber = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 24, nullable: false),
                    ProviderId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    Environment = table.Column<string>(type: "TEXT", maxLength: 16, nullable: true),
                    Outcome = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    SessionReference = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    JourneyId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    StepId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    ProviderReference = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    Message = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    ReusedFromCheckId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    OriginKind = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    OriginId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    OriginName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    TenantId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Kyb_BusinessChecks", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Kyb_Businesses",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Country = table.Column<string>(type: "TEXT", maxLength: 2, nullable: false),
                    RegistrationNumber = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    RegistrationType = table.Column<string>(type: "TEXT", maxLength: 16, nullable: true),
                    RegisteredName = table.Column<string>(type: "TEXT", maxLength: 300, nullable: true),
                    EntityType = table.Column<string>(type: "TEXT", maxLength: 120, nullable: true),
                    RegistrationDate = table.Column<string>(type: "TEXT", maxLength: 40, nullable: true),
                    RegisteredAddress = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    ActivityStatus = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    RawStatus = table.Column<string>(type: "TEXT", maxLength: 80, nullable: true),
                    PeopleJson = table.Column<string>(type: "TEXT", nullable: true),
                    RegistryCheckedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    RegistryCheckId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    RegistryProviderId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    RegistryEnvironment = table.Column<string>(type: "TEXT", maxLength: 16, nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 24, nullable: false),
                    StatusReason = table.Column<string>(type: "TEXT", maxLength: 40, nullable: true),
                    StatusMessage = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    LatestCheckId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DecidedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ReviewedBy = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    ReviewedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ReviewNote = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    TenantId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Kyb_Businesses", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Kyb_BusinessApplicants",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    BusinessId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    SubjectId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    JourneyId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    StepId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    Relationship = table.Column<string>(type: "TEXT", maxLength: 48, nullable: false),
                    EvidenceRequirement = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    ProvidedBusinessName = table.Column<string>(type: "TEXT", maxLength: 300, nullable: true),
                    NameMatched = table.Column<bool>(type: "INTEGER", nullable: true),
                    DirectorMatched = table.Column<bool>(type: "INTEGER", nullable: true),
                    MatchedPersonName = table.Column<string>(type: "TEXT", maxLength: 300, nullable: true),
                    MatchedPersonRole = table.Column<string>(type: "TEXT", maxLength: 120, nullable: true),
                    DocumentIdsJson = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 24, nullable: false),
                    StatusReason = table.Column<string>(type: "TEXT", maxLength: 40, nullable: true),
                    StatusMessage = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    NotifyEmail = table.Column<string>(type: "TEXT", maxLength: 320, nullable: true),
                    ResumeUrl = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DecidedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ReviewedBy = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    ReviewedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ReviewNote = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    TenantId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Kyb_BusinessApplicants", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Kyb_BusinessApplicants_Kyb_Businesses_BusinessId",
                        column: x => x.BusinessId,
                        principalTable: "Kyb_Businesses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Kyb_BusinessApplicants_BusinessId",
                table: "Kyb_BusinessApplicants",
                column: "BusinessId");

            migrationBuilder.CreateIndex(
                name: "IX_Kyb_BusinessApplicants_TenantId_BusinessId_SubjectId",
                table: "Kyb_BusinessApplicants",
                columns: new[] { "TenantId", "BusinessId", "SubjectId" });

            migrationBuilder.CreateIndex(
                name: "IX_Kyb_BusinessApplicants_TenantId_JourneyId",
                table: "Kyb_BusinessApplicants",
                columns: new[] { "TenantId", "JourneyId" });

            migrationBuilder.CreateIndex(
                name: "IX_Kyb_BusinessApplicants_TenantId_Status_UpdatedAt",
                table: "Kyb_BusinessApplicants",
                columns: new[] { "TenantId", "Status", "UpdatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Kyb_BusinessApplicants_TenantId_SubjectId",
                table: "Kyb_BusinessApplicants",
                columns: new[] { "TenantId", "SubjectId" });

            migrationBuilder.CreateIndex(
                name: "IX_Kyb_BusinessChecks_TenantId_BusinessId_CreatedAt",
                table: "Kyb_BusinessChecks",
                columns: new[] { "TenantId", "BusinessId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Kyb_BusinessChecks_TenantId_ProviderId_SessionReference",
                table: "Kyb_BusinessChecks",
                columns: new[] { "TenantId", "ProviderId", "SessionReference" });

            migrationBuilder.CreateIndex(
                name: "IX_Kyb_Businesses_TenantId_Country_RegistrationNumber",
                table: "Kyb_Businesses",
                columns: new[] { "TenantId", "Country", "RegistrationNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Kyb_Businesses_TenantId_Status_UpdatedAt",
                table: "Kyb_Businesses",
                columns: new[] { "TenantId", "Status", "UpdatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Kyb_BusinessApplicants");

            migrationBuilder.DropTable(
                name: "Kyb_BusinessChecks");

            migrationBuilder.DropTable(
                name: "Kyb_Businesses");
        }
    }
}
