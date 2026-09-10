using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Users.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AuthRequestExpiryAndAttempts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The default for ExpiresAt is DateTimeOffset.MinValue, so every auth request that already
            // exists when this migration runs counts as expired and its code stops being accepted.
            migrationBuilder.AddColumn<int>(
                name: "AttemptCount",
                table: "AuthRequest",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CreatedAt",
                table: "AuthRequest",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ExpiresAt",
                table: "AuthRequest",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AttemptCount",
                table: "AuthRequest");

            migrationBuilder.DropColumn(
                name: "CreatedAt",
                table: "AuthRequest");

            migrationBuilder.DropColumn(
                name: "ExpiresAt",
                table: "AuthRequest");
        }
    }
}
