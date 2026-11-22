using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OctaGrid.Data.Sqlite.Migrations;

/// <inheritdoc />
public partial class _20261122074038_InitialCreate : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "OutboxMessages",
            columns: table => new
            {
                Sequence = table.Column<long>(type: "INTEGER", nullable: false)
                    .Annotation("Sqlite:Autoincrement", true),
                EventId = table.Column<Guid>(type: "TEXT", nullable: false),
                Type = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                Payload = table.Column<string>(type: "TEXT", nullable: false),
                OccurredAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                ProcessedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                Attempts = table.Column<int>(type: "INTEGER", nullable: false),
                LastError = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_OutboxMessages", x => x.Sequence);
            });

        migrationBuilder.CreateTable(
            name: "PairingCodes",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                CodeHash = table.Column<byte[]>(type: "BLOB", nullable: false),
                AccountId = table.Column<Guid>(type: "TEXT", nullable: false),
                DeviceId = table.Column<Guid>(type: "TEXT", nullable: true),
                Consumed = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: false),
                AttemptCount = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 0),
                ExpiresAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_PairingCodes", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "Users",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                Email = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                PasswordHash = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                Version = table.Column<long>(type: "INTEGER", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Users", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "Workspaces",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                Version = table.Column<long>(type: "INTEGER", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Workspaces", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "Agents",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                WorkspaceId = table.Column<Guid>(type: "TEXT", nullable: false),
                TokenPrefix = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                TokenHash = table.Column<byte[]>(type: "BLOB", maxLength: 256, nullable: false),
                PublicKey = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                LastSeenAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                Version = table.Column<long>(type: "INTEGER", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Agents", x => x.Id);
                table.ForeignKey(
                    name: "FK_Agents_Workspaces_WorkspaceId",
                    column: x => x.WorkspaceId,
                    principalTable: "Workspaces",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "Dashboards",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                WorkspaceId = table.Column<Guid>(type: "TEXT", nullable: false),
                Type = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                IsDefault = table.Column<bool>(type: "INTEGER", nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                Version = table.Column<long>(type: "INTEGER", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Dashboards", x => x.Id);
                table.ForeignKey(
                    name: "FK_Dashboards_Workspaces_WorkspaceId",
                    column: x => x.WorkspaceId,
                    principalTable: "Workspaces",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "WorkspaceMembers",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                WorkspaceId = table.Column<Guid>(type: "TEXT", nullable: false),
                UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                Role = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                JoinedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                Version = table.Column<long>(type: "INTEGER", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_WorkspaceMembers", x => x.Id);
                table.ForeignKey(
                    name: "FK_WorkspaceMembers_Users_UserId",
                    column: x => x.UserId,
                    principalTable: "Users",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_WorkspaceMembers_Workspaces_WorkspaceId",
                    column: x => x.WorkspaceId,
                    principalTable: "Workspaces",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "AgentStatus",
            columns: table => new
            {
                AgentId = table.Column<Guid>(type: "TEXT", nullable: false),
                State = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                ActivatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                LastConnectedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_AgentStatus", x => x.AgentId);
                table.ForeignKey(
                    name: "FK_AgentStatus_Agents_AgentId",
                    column: x => x.AgentId,
                    principalTable: "Agents",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "Printers",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                WorkspaceId = table.Column<Guid>(type: "TEXT", nullable: false),
                AgentId = table.Column<Guid>(type: "TEXT", nullable: true),
                ReportedModel = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                Enabled = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: false),
                ConnectorId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                Settings = table.Column<string>(type: "TEXT", nullable: false),
                ProtectedSecrets = table.Column<string>(type: "TEXT", nullable: true),
                CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                Version = table.Column<long>(type: "INTEGER", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Printers", x => x.Id);
                table.ForeignKey(
                    name: "FK_Printers_Agents_AgentId",
                    column: x => x.AgentId,
                    principalTable: "Agents",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.SetNull);
                table.ForeignKey(
                    name: "FK_Printers_Workspaces_WorkspaceId",
                    column: x => x.WorkspaceId,
                    principalTable: "Workspaces",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "DashboardWidgets",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                DashboardId = table.Column<Guid>(type: "TEXT", nullable: false),
                PrinterId = table.Column<Guid>(type: "TEXT", nullable: false),
                WidgetType = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                GridX = table.Column<int>(type: "INTEGER", nullable: false),
                GridY = table.Column<int>(type: "INTEGER", nullable: false),
                GridWidth = table.Column<int>(type: "INTEGER", nullable: false),
                GridHeight = table.Column<int>(type: "INTEGER", nullable: false),
                ConfigJson = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                Version = table.Column<long>(type: "INTEGER", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_DashboardWidgets", x => x.Id);
                table.ForeignKey(
                    name: "FK_DashboardWidgets_Dashboards_DashboardId",
                    column: x => x.DashboardId,
                    principalTable: "Dashboards",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_DashboardWidgets_Printers_PrinterId",
                    column: x => x.PrinterId,
                    principalTable: "Printers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "PrinterStatus",
            columns: table => new
            {
                PrinterId = table.Column<Guid>(type: "TEXT", nullable: false),
                State = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                ObservedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                LastSeenAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_PrinterStatus", x => x.PrinterId);
                table.ForeignKey(
                    name: "FK_PrinterStatus_Printers_PrinterId",
                    column: x => x.PrinterId,
                    principalTable: "Printers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "PrintJobs",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                WorkspaceId = table.Column<Guid>(type: "TEXT", nullable: false),
                PrinterId = table.Column<Guid>(type: "TEXT", nullable: false),
                FileName = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                StartedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                EndedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                Outcome = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                Version = table.Column<long>(type: "INTEGER", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_PrintJobs", x => x.Id);
                table.ForeignKey(
                    name: "FK_PrintJobs_Printers_PrinterId",
                    column: x => x.PrinterId,
                    principalTable: "Printers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_Agents_TokenPrefix",
            table: "Agents",
            column: "TokenPrefix");

        migrationBuilder.CreateIndex(
            name: "IX_Agents_WorkspaceId",
            table: "Agents",
            column: "WorkspaceId");

        migrationBuilder.CreateIndex(
            name: "IX_Dashboards_WorkspaceId",
            table: "Dashboards",
            column: "WorkspaceId");

        migrationBuilder.CreateIndex(
            name: "IX_DashboardWidgets_DashboardId",
            table: "DashboardWidgets",
            column: "DashboardId");

        migrationBuilder.CreateIndex(
            name: "IX_DashboardWidgets_PrinterId",
            table: "DashboardWidgets",
            column: "PrinterId");

        migrationBuilder.CreateIndex(
            name: "IX_OutboxMessages_EventId",
            table: "OutboxMessages",
            column: "EventId",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_OutboxMessages_ProcessedAt",
            table: "OutboxMessages",
            column: "ProcessedAt");

        migrationBuilder.CreateIndex(
            name: "IX_PairingCodes_AccountId_Consumed",
            table: "PairingCodes",
            columns: new[] { "AccountId", "Consumed" });

        migrationBuilder.CreateIndex(
            name: "IX_PairingCodes_CodeHash",
            table: "PairingCodes",
            column: "CodeHash",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_PairingCodes_ExpiresAt",
            table: "PairingCodes",
            column: "ExpiresAt");

        migrationBuilder.CreateIndex(
            name: "IX_Printers_AgentId",
            table: "Printers",
            column: "AgentId");

        migrationBuilder.CreateIndex(
            name: "IX_Printers_WorkspaceId",
            table: "Printers",
            column: "WorkspaceId");

        migrationBuilder.CreateIndex(
            name: "IX_PrintJobs_PrinterId_Outcome",
            table: "PrintJobs",
            columns: new[] { "PrinterId", "Outcome" });

        migrationBuilder.CreateIndex(
            name: "IX_PrintJobs_WorkspaceId",
            table: "PrintJobs",
            column: "WorkspaceId");

        migrationBuilder.CreateIndex(
            name: "IX_Users_Email",
            table: "Users",
            column: "Email",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_WorkspaceMembers_UserId",
            table: "WorkspaceMembers",
            column: "UserId");

        migrationBuilder.CreateIndex(
            name: "IX_WorkspaceMembers_WorkspaceId_UserId",
            table: "WorkspaceMembers",
            columns: new[] { "WorkspaceId", "UserId" },
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "AgentStatus");

        migrationBuilder.DropTable(
            name: "DashboardWidgets");

        migrationBuilder.DropTable(
            name: "OutboxMessages");

        migrationBuilder.DropTable(
            name: "PairingCodes");

        migrationBuilder.DropTable(
            name: "PrinterStatus");

        migrationBuilder.DropTable(
            name: "PrintJobs");

        migrationBuilder.DropTable(
            name: "WorkspaceMembers");

        migrationBuilder.DropTable(
            name: "Dashboards");

        migrationBuilder.DropTable(
            name: "Printers");

        migrationBuilder.DropTable(
            name: "Users");

        migrationBuilder.DropTable(
            name: "Agents");

        migrationBuilder.DropTable(
            name: "Workspaces");
    }
}
