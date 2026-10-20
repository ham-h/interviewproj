using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VoiceHire.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class QuestionImport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CreatedAt",
                table: "Questions",
                type: "datetimeoffset",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AddColumn<string>(
                name: "CreatedBy",
                table: "Questions",
                type: "nvarchar(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Source",
                table: "Questions",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Seeded");

            migrationBuilder.AddColumn<string>(
                name: "SourceDocument",
                table: "Questions",
                type: "nvarchar(260)",
                maxLength: 260,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CreatedAt",
                table: "Questions");

            migrationBuilder.DropColumn(
                name: "CreatedBy",
                table: "Questions");

            migrationBuilder.DropColumn(
                name: "Source",
                table: "Questions");

            migrationBuilder.DropColumn(
                name: "SourceDocument",
                table: "Questions");
        }
    }
}
