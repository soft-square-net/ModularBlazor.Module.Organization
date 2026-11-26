using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace soft.square.net.WebApi.Modules.Organization.Infrastructure.Data.Replica.Migrations.Oracle
{
    /// <inheritdoc />
    public partial class InitialReplica : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "organization_integration_inbox",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "RAW(16)", nullable: false),
                    Type = table.Column<string>(type: "NVARCHAR2(200)", maxLength: 200, nullable: false),
                    Source = table.Column<string>(type: "NVARCHAR2(100)", maxLength: 100, nullable: false),
                    ProcessedAt = table.Column<DateTimeOffset>(type: "TIMESTAMP(7) WITH TIME ZONE", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_organization_integration_inbox", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "organization_integration_outbox",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "RAW(16)", nullable: false),
                    Type = table.Column<string>(type: "NVARCHAR2(200)", maxLength: 200, nullable: false),
                    TenantId = table.Column<string>(type: "NVARCHAR2(64)", maxLength: 64, nullable: false),
                    Envelope = table.Column<string>(type: "NVARCHAR2(2000)", nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "TIMESTAMP(7) WITH TIME ZONE", nullable: false),
                    SentAt = table.Column<DateTimeOffset>(type: "TIMESTAMP(7) WITH TIME ZONE", nullable: true),
                    Attempts = table.Column<int>(type: "NUMBER(10)", nullable: false),
                    LastError = table.Column<string>(type: "NVARCHAR2(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_organization_integration_outbox", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "organization_replica_links",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "RAW(16)", nullable: false),
                    OrgId = table.Column<Guid>(type: "RAW(16)", nullable: false),
                    FromSeedId = table.Column<string>(type: "NVARCHAR2(64)", maxLength: 64, nullable: false),
                    FromUnitId = table.Column<Guid>(type: "RAW(16)", nullable: false),
                    ToSeedId = table.Column<string>(type: "NVARCHAR2(64)", maxLength: 64, nullable: false),
                    ToUnitId = table.Column<Guid>(type: "RAW(16)", nullable: false),
                    State = table.Column<string>(type: "NVARCHAR2(20)", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_organization_replica_links", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "organization_replica_memberships",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "RAW(16)", nullable: false),
                    OrgId = table.Column<Guid>(type: "RAW(16)", nullable: false),
                    PersonId = table.Column<Guid>(type: "RAW(16)", nullable: false),
                    Issuer = table.Column<string>(type: "NVARCHAR2(256)", maxLength: 256, nullable: false),
                    Subject = table.Column<string>(type: "NVARCHAR2(128)", maxLength: 128, nullable: false),
                    UnitId = table.Column<Guid>(type: "RAW(16)", nullable: false),
                    PositionId = table.Column<Guid>(type: "RAW(16)", nullable: true),
                    PositionKey = table.Column<string>(type: "NVARCHAR2(40)", maxLength: 40, nullable: true),
                    Permissions = table.Column<string>(type: "NVARCHAR2(2000)", nullable: false),
                    From = table.Column<DateTimeOffset>(type: "TIMESTAMP(7) WITH TIME ZONE", nullable: false),
                    To = table.Column<DateTimeOffset>(type: "TIMESTAMP(7) WITH TIME ZONE", nullable: true),
                    Primary = table.Column<bool>(type: "BOOLEAN", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_organization_replica_memberships", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "organization_replica_persons",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "RAW(16)", nullable: false),
                    OrgId = table.Column<Guid>(type: "RAW(16)", nullable: false),
                    Issuer = table.Column<string>(type: "NVARCHAR2(256)", maxLength: 256, nullable: false),
                    Subject = table.Column<string>(type: "NVARCHAR2(128)", maxLength: 128, nullable: false),
                    DisplayName = table.Column<string>(type: "NVARCHAR2(200)", maxLength: 200, nullable: false),
                    Email = table.Column<string>(type: "NVARCHAR2(256)", maxLength: 256, nullable: true),
                    IsEmployee = table.Column<bool>(type: "BOOLEAN", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_organization_replica_persons", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "organization_replica_positions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "RAW(16)", nullable: false),
                    OrgId = table.Column<Guid>(type: "RAW(16)", nullable: false),
                    Key = table.Column<string>(type: "NVARCHAR2(40)", maxLength: 40, nullable: false),
                    NameEn = table.Column<string>(type: "NVARCHAR2(200)", maxLength: 200, nullable: false),
                    NameAr = table.Column<string>(type: "NVARCHAR2(200)", maxLength: 200, nullable: false),
                    Permissions = table.Column<string>(type: "NVARCHAR2(2000)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_organization_replica_positions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "organization_replica_sync",
                columns: table => new
                {
                    Id = table.Column<int>(type: "NUMBER(10)", nullable: false),
                    SeedId = table.Column<string>(type: "NVARCHAR2(64)", maxLength: 64, nullable: false),
                    OrgId = table.Column<Guid>(type: "RAW(16)", nullable: false),
                    RootUnitId = table.Column<Guid>(type: "RAW(16)", nullable: false),
                    Cursor = table.Column<long>(type: "NUMBER(19)", nullable: false),
                    Applied = table.Column<string>(type: "NVARCHAR2(2000)", nullable: false),
                    PendingGrantUnits = table.Column<string>(type: "NVARCHAR2(2000)", nullable: false),
                    RebuildMemberships = table.Column<bool>(type: "BOOLEAN", nullable: false),
                    LastSyncedAt = table.Column<DateTimeOffset>(type: "TIMESTAMP(7) WITH TIME ZONE", nullable: true),
                    LastError = table.Column<string>(type: "NVARCHAR2(400)", maxLength: 400, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_organization_replica_sync", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "organization_replica_units",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "RAW(16)", nullable: false),
                    OrgId = table.Column<Guid>(type: "RAW(16)", nullable: false),
                    ParentId = table.Column<Guid>(type: "RAW(16)", nullable: true),
                    TypeKey = table.Column<string>(type: "NVARCHAR2(40)", maxLength: 40, nullable: false),
                    Path = table.Column<string>(type: "NVARCHAR2(1024)", maxLength: 1024, nullable: false),
                    NameEn = table.Column<string>(type: "NVARCHAR2(200)", maxLength: 200, nullable: false),
                    NameAr = table.Column<string>(type: "NVARCHAR2(200)", maxLength: 200, nullable: false),
                    Tags = table.Column<string>(type: "NVARCHAR2(2000)", nullable: false),
                    TimeZone = table.Column<string>(type: "NVARCHAR2(100)", maxLength: 100, nullable: true),
                    Categories = table.Column<string>(type: "NVARCHAR2(2000)", nullable: false),
                    Binding = table.Column<string>(type: "NVARCHAR2(20)", maxLength: 20, nullable: false),
                    SeedId = table.Column<string>(type: "NVARCHAR2(64)", maxLength: 64, nullable: true),
                    Version = table.Column<int>(type: "NUMBER(10)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_organization_replica_units", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_organization_integration_inbox_ProcessedAt",
                table: "organization_integration_inbox",
                column: "ProcessedAt");

            migrationBuilder.CreateIndex(
                name: "IX_organization_integration_outbox_SentAt_OccurredAt",
                table: "organization_integration_outbox",
                columns: new[] { "SentAt", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_organization_replica_memberships_PersonId",
                table: "organization_replica_memberships",
                column: "PersonId");

            migrationBuilder.CreateIndex(
                name: "IX_organization_replica_memberships_PositionId",
                table: "organization_replica_memberships",
                column: "PositionId");

            migrationBuilder.CreateIndex(
                name: "IX_organization_replica_memberships_UnitId",
                table: "organization_replica_memberships",
                column: "UnitId");

            migrationBuilder.CreateIndex(
                name: "IX_organization_replica_persons_Subject",
                table: "organization_replica_persons",
                column: "Subject");

            migrationBuilder.CreateIndex(
                name: "IX_organization_replica_units_ParentId",
                table: "organization_replica_units",
                column: "ParentId");

            migrationBuilder.CreateIndex(
                name: "IX_organization_replica_units_Path",
                table: "organization_replica_units",
                column: "Path");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "organization_integration_inbox");

            migrationBuilder.DropTable(
                name: "organization_integration_outbox");

            migrationBuilder.DropTable(
                name: "organization_replica_links");

            migrationBuilder.DropTable(
                name: "organization_replica_memberships");

            migrationBuilder.DropTable(
                name: "organization_replica_persons");

            migrationBuilder.DropTable(
                name: "organization_replica_positions");

            migrationBuilder.DropTable(
                name: "organization_replica_sync");

            migrationBuilder.DropTable(
                name: "organization_replica_units");
        }
    }
}
