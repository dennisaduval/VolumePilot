using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VolumePilot.HQ.Api.Persistence.Migrations;

public partial class PlanningCorrections : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        foreach (var table in new[] { "client_organizations", "jobs", "events" })
        {
            migrationBuilder.AddColumn<long>("Revision", table, type: "bigint", nullable: false, defaultValue: 1L);
            migrationBuilder.AddColumn<DateTime>("UpdatedAtUtc", table, type: "timestamp with time zone", nullable: true);
        }

        migrationBuilder.CreateTable("activity_records", columns: table => new
        {
            Id = table.Column<string>(type: "character varying(26)", maxLength: 26, nullable: false),
            TenantId = table.Column<string>(type: "character varying(26)", maxLength: 26, nullable: false),
            ActorUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
            EntityType = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
            EntityId = table.Column<string>(type: "character varying(26)", maxLength: 26, nullable: false),
            Action = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
            BeforeJson = table.Column<string>(type: "text", nullable: false),
            AfterJson = table.Column<string>(type: "text", nullable: false),
            OccurredAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
        }, constraints: table =>
        {
            table.PrimaryKey("PK_activity_records", x => x.Id);
            table.ForeignKey("FK_activity_records_company_accounts_TenantId", x => x.TenantId,
                "company_accounts", "Id", onDelete: ReferentialAction.Restrict);
            table.ForeignKey("FK_activity_records_AspNetUsers_ActorUserId", x => x.ActorUserId,
                "AspNetUsers", "Id", onDelete: ReferentialAction.Restrict);
        });
        migrationBuilder.CreateIndex("IX_activity_records_ActorUserId", "activity_records", "ActorUserId");
        migrationBuilder.CreateIndex("IX_activity_records_TenantId_EntityType_EntityId_OccurredAtUtc",
            "activity_records", new[] { "TenantId", "EntityType", "EntityId", "OccurredAtUtc" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable("activity_records");
        foreach (var table in new[] { "client_organizations", "jobs", "events" })
        {
            migrationBuilder.DropColumn("Revision", table);
            migrationBuilder.DropColumn("UpdatedAtUtc", table);
        }
    }
}
