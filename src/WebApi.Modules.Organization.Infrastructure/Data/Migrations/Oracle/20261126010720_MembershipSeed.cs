using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace soft.square.net.WebApi.Modules.Organization.Infrastructure.Data.Migrations.Oracle
{
    /// <inheritdoc />
    public partial class MembershipSeed : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SeedId",
                table: "organization_memberships",
                type: "NVARCHAR2(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_organization_memberships_SeedId",
                table: "organization_memberships",
                column: "SeedId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_organization_memberships_SeedId",
                table: "organization_memberships");

            migrationBuilder.DropColumn(
                name: "SeedId",
                table: "organization_memberships");
        }
    }
}
