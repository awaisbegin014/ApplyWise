using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ApplyWise.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddGmailOAuthFlowFence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "GmailOAuthFlows",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    FlowId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GmailOAuthFlows", x => x.UserId);
                    table.ForeignKey(
                        name: "FK_GmailOAuthFlows_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GmailOAuthFlows");
        }
    }
}
