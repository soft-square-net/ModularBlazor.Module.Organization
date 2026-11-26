using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace soft.square.net.WebApi.Modules.Organization.Infrastructure.Data.Migrations.PostgreSql
{
    /// <inheritdoc />
    public partial class MembershipSeed : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SeedId",
                schema: "organization",
                table: "memberships",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_memberships_SeedId",
                schema: "organization",
                table: "memberships",
                column: "SeedId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_memberships_SeedId",
                schema: "organization",
                table: "memberships");

            migrationBuilder.DropColumn(
                name: "SeedId",
                schema: "organization",
                table: "memberships");
        }
    }
}
