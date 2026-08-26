using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ApplyWise.Web.Migrations
{
    /// <inheritdoc />
    public partial class ConsolidateAtsAiAndResumeBuildLimits : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AiFeedbackJson",
                table: "ResumeAnalyses",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "AiGeneratedAt",
                table: "ResumeAnalyses",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AiModel",
                table: "ResumeAnalyses",
                type: "nvarchar(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ResumeBuildUsageRecords",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    TemplateId = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ResumeBuildUsageRecords", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ResumeBuildUsageRecords_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ResumeBuildUsageRecords_UserId_CreatedAt",
                table: "ResumeBuildUsageRecords",
                columns: new[] { "UserId", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ResumeBuildUsageRecords");

            migrationBuilder.DropColumn(
                name: "AiFeedbackJson",
                table: "ResumeAnalyses");

            migrationBuilder.DropColumn(
                name: "AiGeneratedAt",
                table: "ResumeAnalyses");

            migrationBuilder.DropColumn(
                name: "AiModel",
                table: "ResumeAnalyses");
        }
    }
}
