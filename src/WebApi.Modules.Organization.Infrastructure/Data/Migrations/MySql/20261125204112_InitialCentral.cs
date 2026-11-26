using System;
using Microsoft.EntityFrameworkCore.Migrations;
using MySql.EntityFrameworkCore.Metadata;

#nullable disable

namespace soft.square.net.WebApi.Modules.Organization.Infrastructure.Data.Migrations.MySql
{
    /// <inheritdoc />
    public partial class InitialCentral : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "organization_feed",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    OrgId = table.Column<Guid>(type: "char(36)", nullable: false),
                    Type = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false),
                    Subject = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false),
                    UnitPath = table.Column<string>(type: "varchar(1024)", maxLength: 1024, nullable: true),
                    OccurredAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    Data = table.Column<string>(type: "longtext", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_organization_feed", x => x.Id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "organization_memberships",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    OrgId = table.Column<Guid>(type: "char(36)", nullable: false),
                    PersonId = table.Column<Guid>(type: "char(36)", nullable: false),
                    UnitId = table.Column<Guid>(type: "char(36)", nullable: false),
                    PositionId = table.Column<Guid>(type: "char(36)", nullable: true),
                    From = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    To = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    Primary = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_organization_memberships", x => x.Id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "organization_orgs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    Key = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false),
                    NameEn = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false),
                    NameAr = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false),
                    RootUnitId = table.Column<Guid>(type: "char(36)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_organization_orgs", x => x.Id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "organization_persons",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    OrgId = table.Column<Guid>(type: "char(36)", nullable: false),
                    Issuer = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: false),
                    Subject = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false),
                    DisplayName = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false),
                    Email = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: true),
                    IsEmployee = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    EmployeeNumber = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true),
                    JobTitle = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: true),
                    HiredOn = table.Column<DateOnly>(type: "date", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_organization_persons", x => x.Id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "organization_positions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    OrgId = table.Column<Guid>(type: "char(36)", nullable: false),
                    Key = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false),
                    NameEn = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false),
                    NameAr = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false),
                    Permissions = table.Column<string>(type: "longtext", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_organization_positions", x => x.Id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "organization_tenant_links",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    OrgId = table.Column<Guid>(type: "char(36)", nullable: false),
                    FromSeedId = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    FromUnitId = table.Column<Guid>(type: "char(36)", nullable: false),
                    ToSeedId = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    ToUnitId = table.Column<Guid>(type: "char(36)", nullable: false),
                    State = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                    RequestedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    DecidedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    RevokedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_organization_tenant_links", x => x.Id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "organization_tenant_seeds",
                columns: table => new
                {
                    Id = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    OrgId = table.Column<Guid>(type: "char(36)", nullable: false),
                    RootUnitId = table.Column<Guid>(type: "char(36)", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    DeploymentId = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false),
                    TenantId = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false),
                    Name = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false),
                    DatabaseServer = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false),
                    DatabaseName = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false),
                    Status = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                    Reason = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: true),
                    ReportedVersion = table.Column<long>(type: "bigint", nullable: true),
                    ActualDatabaseServer = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true),
                    ActualDatabaseName = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    Revision = table.Column<int>(type: "int", nullable: false),
                    AdminPersonIds = table.Column<string>(type: "longtext", nullable: false),
                    Hosts = table.Column<string>(type: "longtext", nullable: false),
                    Modules = table.Column<string>(type: "longtext", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_organization_tenant_seeds", x => x.Id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "organization_unit_types",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    OrgId = table.Column<Guid>(type: "char(36)", nullable: false),
                    Key = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false),
                    NameEn = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false),
                    NameAr = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false),
                    HoldsCategories = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    Order = table.Column<int>(type: "int", nullable: false),
                    AllowedParents = table.Column<string>(type: "longtext", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_organization_unit_types", x => x.Id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "organization_units",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    OrgId = table.Column<Guid>(type: "char(36)", nullable: false),
                    TypeId = table.Column<Guid>(type: "char(36)", nullable: false),
                    TypeKey = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false),
                    ParentId = table.Column<Guid>(type: "char(36)", nullable: true),
                    Path = table.Column<string>(type: "varchar(1024)", maxLength: 1024, nullable: false),
                    Depth = table.Column<int>(type: "int", nullable: false),
                    NameEn = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false),
                    NameAr = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false),
                    TimeZone = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true),
                    Binding = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                    SeedId = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false),
                    Tags = table.Column<string>(type: "longtext", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_organization_units", x => x.Id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "organization_org_admins",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    Issuer = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: false),
                    Subject = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false),
                    OrgId = table.Column<Guid>(type: "char(36)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_organization_org_admins", x => x.Id);
                    table.ForeignKey(
                        name: "FK_organization_org_admins_organization_orgs_OrgId",
                        column: x => x.OrgId,
                        principalTable: "organization_orgs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "organization_tenant_seed_history",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    At = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    Status = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                    Reason = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: true),
                    ActualDatabaseServer = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true),
                    ActualDatabaseName = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true),
                    SeedId = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_organization_tenant_seed_history", x => x.Id);
                    table.ForeignKey(
                        name: "FK_organization_tenant_seed_history_organization_tenant_seeds_S~",
                        column: x => x.SeedId,
                        principalTable: "organization_tenant_seeds",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "organization_unit_categories",
                columns: table => new
                {
                    CategoryId = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false),
                    UnitId = table.Column<Guid>(type: "char(36)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_organization_unit_categories", x => new { x.UnitId, x.CategoryId });
                    table.ForeignKey(
                        name: "FK_organization_unit_categories_organization_units_UnitId",
                        column: x => x.UnitId,
                        principalTable: "organization_units",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_organization_feed_OrgId_Id",
                table: "organization_feed",
                columns: new[] { "OrgId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_organization_memberships_OrgId_UnitId",
                table: "organization_memberships",
                columns: new[] { "OrgId", "UnitId" });

            migrationBuilder.CreateIndex(
                name: "IX_organization_memberships_PersonId",
                table: "organization_memberships",
                column: "PersonId");

            migrationBuilder.CreateIndex(
                name: "IX_organization_memberships_PositionId",
                table: "organization_memberships",
                column: "PositionId");

            migrationBuilder.CreateIndex(
                name: "IX_organization_org_admins_OrgId",
                table: "organization_org_admins",
                column: "OrgId");

            migrationBuilder.CreateIndex(
                name: "IX_organization_org_admins_Subject",
                table: "organization_org_admins",
                column: "Subject");

            migrationBuilder.CreateIndex(
                name: "IX_organization_orgs_Key",
                table: "organization_orgs",
                column: "Key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_organization_persons_OrgId_Subject_Issuer",
                table: "organization_persons",
                columns: new[] { "OrgId", "Subject", "Issuer" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_organization_persons_Subject",
                table: "organization_persons",
                column: "Subject");

            migrationBuilder.CreateIndex(
                name: "IX_organization_positions_OrgId_Key",
                table: "organization_positions",
                columns: new[] { "OrgId", "Key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_organization_tenant_links_OrgId",
                table: "organization_tenant_links",
                column: "OrgId");

            migrationBuilder.CreateIndex(
                name: "IX_organization_tenant_seed_history_SeedId",
                table: "organization_tenant_seed_history",
                column: "SeedId");

            migrationBuilder.CreateIndex(
                name: "IX_organization_tenant_seeds_DeploymentId_TenantId",
                table: "organization_tenant_seeds",
                columns: new[] { "DeploymentId", "TenantId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_organization_tenant_seeds_OrgId",
                table: "organization_tenant_seeds",
                column: "OrgId");

            migrationBuilder.CreateIndex(
                name: "IX_organization_tenant_seeds_RootUnitId",
                table: "organization_tenant_seeds",
                column: "RootUnitId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_organization_unit_categories_CategoryId",
                table: "organization_unit_categories",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_organization_unit_types_OrgId_Key",
                table: "organization_unit_types",
                columns: new[] { "OrgId", "Key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_organization_units_OrgId_ParentId",
                table: "organization_units",
                columns: new[] { "OrgId", "ParentId" });

            migrationBuilder.CreateIndex(
                name: "IX_organization_units_OrgId_Path",
                table: "organization_units",
                columns: new[] { "OrgId", "Path" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "organization_feed");

            migrationBuilder.DropTable(
                name: "organization_memberships");

            migrationBuilder.DropTable(
                name: "organization_org_admins");

            migrationBuilder.DropTable(
                name: "organization_persons");

            migrationBuilder.DropTable(
                name: "organization_positions");

            migrationBuilder.DropTable(
                name: "organization_tenant_links");

            migrationBuilder.DropTable(
                name: "organization_tenant_seed_history");

            migrationBuilder.DropTable(
                name: "organization_unit_categories");

            migrationBuilder.DropTable(
                name: "organization_unit_types");

            migrationBuilder.DropTable(
                name: "organization_orgs");

            migrationBuilder.DropTable(
                name: "organization_tenant_seeds");

            migrationBuilder.DropTable(
                name: "organization_units");
        }
    }
}
