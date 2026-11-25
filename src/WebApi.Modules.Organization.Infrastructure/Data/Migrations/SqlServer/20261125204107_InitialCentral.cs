using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace soft.square.net.WebApi.Modules.Organization.Infrastructure.Data.Migrations.SqlServer
{
    /// <inheritdoc />
    public partial class InitialCentral : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "organization");

            migrationBuilder.CreateTable(
                name: "feed",
                schema: "organization",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrgId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Type = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Subject = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    UnitPath = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: true),
                    OccurredAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Data = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_feed", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "memberships",
                schema: "organization",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrgId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PersonId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UnitId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PositionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    From = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    To = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Primary = table.Column<bool>(type: "bit", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_memberships", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "orgs",
                schema: "organization",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Key = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    NameEn = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    NameAr = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    RootUnitId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_orgs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "persons",
                schema: "organization",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrgId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Issuer = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    Subject = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Email = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    IsEmployee = table.Column<bool>(type: "bit", nullable: false),
                    EmployeeNumber = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    JobTitle = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    HiredOn = table.Column<DateOnly>(type: "date", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_persons", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "positions",
                schema: "organization",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrgId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Key = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    NameEn = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    NameAr = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false),
                    Permissions = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_positions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "tenant_links",
                schema: "organization",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrgId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FromSeedId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    FromUnitId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ToSeedId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ToUnitId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    State = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    RequestedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    DecidedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RevokedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tenant_links", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "tenant_seeds",
                schema: "organization",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    OrgId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RootUnitId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    DeploymentId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    TenantId = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    DatabaseServer = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    DatabaseName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ReportedVersion = table.Column<long>(type: "bigint", nullable: true),
                    ActualDatabaseServer = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ActualDatabaseName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Revision = table.Column<int>(type: "int", nullable: false),
                    AdminPersonIds = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Hosts = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Modules = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tenant_seeds", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "unit_types",
                schema: "organization",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrgId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Key = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    NameEn = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    NameAr = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    HoldsCategories = table.Column<bool>(type: "bit", nullable: false),
                    Order = table.Column<int>(type: "int", nullable: false),
                    AllowedParents = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_unit_types", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "units",
                schema: "organization",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrgId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TypeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TypeKey = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    ParentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Path = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: false),
                    Depth = table.Column<int>(type: "int", nullable: false),
                    NameEn = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    NameAr = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    TimeZone = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Binding = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    SeedId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false),
                    Tags = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_units", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "org_admins",
                schema: "organization",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Issuer = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    Subject = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    OrgId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_org_admins", x => x.Id);
                    table.ForeignKey(
                        name: "FK_org_admins_orgs_OrgId",
                        column: x => x.OrgId,
                        principalSchema: "organization",
                        principalTable: "orgs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "tenant_seed_history",
                schema: "organization",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    At = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ActualDatabaseServer = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ActualDatabaseName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    SeedId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tenant_seed_history", x => x.Id);
                    table.ForeignKey(
                        name: "FK_tenant_seed_history_tenant_seeds_SeedId",
                        column: x => x.SeedId,
                        principalSchema: "organization",
                        principalTable: "tenant_seeds",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "unit_categories",
                schema: "organization",
                columns: table => new
                {
                    CategoryId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    UnitId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_unit_categories", x => new { x.UnitId, x.CategoryId });
                    table.ForeignKey(
                        name: "FK_unit_categories_units_UnitId",
                        column: x => x.UnitId,
                        principalSchema: "organization",
                        principalTable: "units",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_feed_OrgId_Id",
                schema: "organization",
                table: "feed",
                columns: new[] { "OrgId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_memberships_OrgId_UnitId",
                schema: "organization",
                table: "memberships",
                columns: new[] { "OrgId", "UnitId" });

            migrationBuilder.CreateIndex(
                name: "IX_memberships_PersonId",
                schema: "organization",
                table: "memberships",
                column: "PersonId");

            migrationBuilder.CreateIndex(
                name: "IX_memberships_PositionId",
                schema: "organization",
                table: "memberships",
                column: "PositionId");

            migrationBuilder.CreateIndex(
                name: "IX_org_admins_OrgId",
                schema: "organization",
                table: "org_admins",
                column: "OrgId");

            migrationBuilder.CreateIndex(
                name: "IX_org_admins_Subject",
                schema: "organization",
                table: "org_admins",
                column: "Subject");

            migrationBuilder.CreateIndex(
                name: "IX_orgs_Key",
                schema: "organization",
                table: "orgs",
                column: "Key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_persons_OrgId_Subject_Issuer",
                schema: "organization",
                table: "persons",
                columns: new[] { "OrgId", "Subject", "Issuer" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_persons_Subject",
                schema: "organization",
                table: "persons",
                column: "Subject");

            migrationBuilder.CreateIndex(
                name: "IX_positions_OrgId_Key",
                schema: "organization",
                table: "positions",
                columns: new[] { "OrgId", "Key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_tenant_links_OrgId",
                schema: "organization",
                table: "tenant_links",
                column: "OrgId");

            migrationBuilder.CreateIndex(
                name: "IX_tenant_seed_history_SeedId",
                schema: "organization",
                table: "tenant_seed_history",
                column: "SeedId");

            migrationBuilder.CreateIndex(
                name: "IX_tenant_seeds_DeploymentId_TenantId",
                schema: "organization",
                table: "tenant_seeds",
                columns: new[] { "DeploymentId", "TenantId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_tenant_seeds_OrgId",
                schema: "organization",
                table: "tenant_seeds",
                column: "OrgId");

            migrationBuilder.CreateIndex(
                name: "IX_tenant_seeds_RootUnitId",
                schema: "organization",
                table: "tenant_seeds",
                column: "RootUnitId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_unit_categories_CategoryId",
                schema: "organization",
                table: "unit_categories",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_unit_types_OrgId_Key",
                schema: "organization",
                table: "unit_types",
                columns: new[] { "OrgId", "Key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_units_OrgId_ParentId",
                schema: "organization",
                table: "units",
                columns: new[] { "OrgId", "ParentId" });

            migrationBuilder.CreateIndex(
                name: "IX_units_OrgId_Path",
                schema: "organization",
                table: "units",
                columns: new[] { "OrgId", "Path" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "feed",
                schema: "organization");

            migrationBuilder.DropTable(
                name: "memberships",
                schema: "organization");

            migrationBuilder.DropTable(
                name: "org_admins",
                schema: "organization");

            migrationBuilder.DropTable(
                name: "persons",
                schema: "organization");

            migrationBuilder.DropTable(
                name: "positions",
                schema: "organization");

            migrationBuilder.DropTable(
                name: "tenant_links",
                schema: "organization");

            migrationBuilder.DropTable(
                name: "tenant_seed_history",
                schema: "organization");

            migrationBuilder.DropTable(
                name: "unit_categories",
                schema: "organization");

            migrationBuilder.DropTable(
                name: "unit_types",
                schema: "organization");

            migrationBuilder.DropTable(
                name: "orgs",
                schema: "organization");

            migrationBuilder.DropTable(
                name: "tenant_seeds",
                schema: "organization");

            migrationBuilder.DropTable(
                name: "units",
                schema: "organization");
        }
    }
}
