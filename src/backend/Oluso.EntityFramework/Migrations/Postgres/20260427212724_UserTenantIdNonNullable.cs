using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Oluso.EntityFramework.Migrations.Postgres
{
    /// <inheritdoc />
    public partial class UserTenantIdNonNullable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ExternalProvisioningId",
                table: "CustomDomainVerifications",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "GracePeriodStartedAt",
                table: "CustomDomainVerifications",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastRevalidatedAt",
                table: "CustomDomainVerifications",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "TenantId",
                table: "AspNetUsers",
                type: "character varying(128)",
                maxLength: 128,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(128)",
                oldMaxLength: 128,
                oldNullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ExternalProvisioningId",
                table: "CustomDomainVerifications");

            migrationBuilder.DropColumn(
                name: "GracePeriodStartedAt",
                table: "CustomDomainVerifications");

            migrationBuilder.DropColumn(
                name: "LastRevalidatedAt",
                table: "CustomDomainVerifications");

            migrationBuilder.AlterColumn<string>(
                name: "TenantId",
                table: "AspNetUsers",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(128)",
                oldMaxLength: 128);
        }
    }
}
