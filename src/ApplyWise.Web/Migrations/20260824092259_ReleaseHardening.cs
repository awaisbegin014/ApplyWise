using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ApplyWise.Web.Migrations
{
    /// <inheritdoc />
    public partial class ReleaseHardening : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The previous six-digit hashes are intentionally invalidated because their
            // low-entropy value can be brute-forced from a database-only compromise.
            migrationBuilder.Sql("DELETE FROM [AccountSecurityCodes]");

            // Keep the legacy columns for one rollback window. Defaults let the
            // new binary omit them while an older binary can still read the row
            // safely and reject it as an invalid legacy code.
            migrationBuilder.Sql(
                "ALTER TABLE [AccountSecurityCodes] ADD CONSTRAINT "
                + "[DF_AccountSecurityCodes_CodeHash_Rollback] DEFAULT "
                + "0x0000000000000000000000000000000000000000000000000000000000000000 FOR [CodeHash]");
            migrationBuilder.Sql(
                "ALTER TABLE [AccountSecurityCodes] ADD CONSTRAINT "
                + "[DF_AccountSecurityCodes_Salt_Rollback] DEFAULT "
                + "0x00000000000000000000000000000000 FOR [Salt]");

            migrationBuilder.AddColumn<long>(
                name: "SnapshotSizeBytes",
                table: "ResumeAnalyses",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<byte[]>(
                name: "ProtectedCode",
                table: "AccountSecurityCodes",
                type: "varbinary(512)",
                nullable: false,
                defaultValue: new byte[0]);

            migrationBuilder.Sql(
                "UPDATE [ResumeAnalyses] SET [SnapshotSizeBytes] = "
                + "COALESCE(DATALENGTH([ResumeTextSnapshot]), 0) + "
                + "COALESCE(DATALENGTH([JobDescriptionSnapshot]), 0)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DELETE FROM [AccountSecurityCodes]");

            migrationBuilder.Sql(
                "ALTER TABLE [AccountSecurityCodes] DROP CONSTRAINT [DF_AccountSecurityCodes_CodeHash_Rollback]");
            migrationBuilder.Sql(
                "ALTER TABLE [AccountSecurityCodes] DROP CONSTRAINT [DF_AccountSecurityCodes_Salt_Rollback]");

            migrationBuilder.DropColumn(
                name: "SnapshotSizeBytes",
                table: "ResumeAnalyses");

            migrationBuilder.DropColumn(
                name: "ProtectedCode",
                table: "AccountSecurityCodes");

        }
    }
}
