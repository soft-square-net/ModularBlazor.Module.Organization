using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace soft.square.net.WebApi.Modules.Organization.Infrastructure.Data.Replica.Migrations.SqlServer
{
    /// <inheritdoc />
    public partial class InitialReplica : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "organization");

            migrationBuilder.CreateTable(
                name: "integration_inbox",
                schema: "organization",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Type = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Source = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ProcessedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_integration_inbox", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "integration_outbox",
                schema: "organization",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Type = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    TenantId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Envelope = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    SentAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Attempts = table.Column<int>(type: "int", nullable: false),
                    LastError = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_integration_outbox", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "replica_links",
                schema: "organization",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrgId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FromSeedId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    FromUnitId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ToSeedId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ToUnitId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    State = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_replica_links", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "replica_memberships",
                schema: "organization",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrgId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PersonId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Issuer = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    Subject = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    UnitId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PositionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PositionKey = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    Permissions = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    From = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    To = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Primary = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_replica_memberships", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "replica_persons",
                schema: "organization",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrgId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Issuer = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    Subject = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Email = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    IsEmployee = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_replica_persons", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "replica_positions",
                schema: "organization",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrgId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Key = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    NameEn = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    NameAr = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Permissions = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_replica_positions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "replica_sync",
                schema: "organization",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    SeedId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    OrgId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RootUnitId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Cursor = table.Column<long>(type: "bigint", nullable: false),
                    Applied = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    PendingGrantUnits = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    RebuildMemberships = table.Column<bool>(type: "bit", nullable: false),
                    LastSyncedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    LastError = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_replica_sync", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "replica_units",
                schema: "organization",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrgId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ParentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TypeKey = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Path = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: false),
                    NameEn = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    NameAr = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Tags = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    TimeZone = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Categories = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Binding = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    SeedId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    Version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_replica_units", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_integration_inbox_ProcessedAt",
                schema: "organization",
                table: "integration_inbox",
                column: "ProcessedAt");

            migrationBuilder.CreateIndex(
                name: "IX_integration_outbox_SentAt_OccurredAt",
                schema: "organization",
                table: "integration_outbox",
                columns: new[] { "SentAt", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_replica_memberships_PersonId",
                schema: "organization",
                table: "replica_memberships",
                column: "PersonId");

            migrationBuilder.CreateIndex(
                name: "IX_replica_memberships_PositionId",
                schema: "organization",
                table: "replica_memberships",
                column: "PositionId");

            migrationBuilder.CreateIndex(
                name: "IX_replica_memberships_UnitId",
                schema: "organization",
                table: "replica_memberships",
                column: "UnitId");

            migrationBuilder.CreateIndex(
                name: "IX_replica_persons_Subject",
                schema: "organization",
                table: "replica_persons",
                column: "Subject");

            migrationBuilder.CreateIndex(
                name: "IX_replica_units_ParentId",
                schema: "organization",
                table: "replica_units",
                column: "ParentId");

            migrationBuilder.CreateIndex(
                name: "IX_replica_units_Path",
                schema: "organization",
                table: "replica_units",
                column: "Path");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "integration_inbox",
                schema: "organization");

            migrationBuilder.DropTable(
                name: "integration_outbox",
                schema: "organization");

            migrationBuilder.DropTable(
                name: "replica_links",
                schema: "organization");

            migrationBuilder.DropTable(
                name: "replica_memberships",
                schema: "organization");

            migrationBuilder.DropTable(
                name: "replica_persons",
                schema: "organization");

            migrationBuilder.DropTable(
                name: "replica_positions",
                schema: "organization");

            migrationBuilder.DropTable(
                name: "replica_sync",
                schema: "organization");

            migrationBuilder.DropTable(
                name: "replica_units",
                schema: "organization");
        }
    }
}
