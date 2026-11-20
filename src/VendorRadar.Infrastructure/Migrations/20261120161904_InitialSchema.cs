using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VendorRadar.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Organizations",
                columns: table => new
                {
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Organizations", x => new { x.TenantId, x.Id });
                    table.UniqueConstraint("AK_Organizations_TenantId", x => x.TenantId);
                    table.CheckConstraint("CK_Organizations_Tenant", "[Id] = [TenantId]");
                });

            migrationBuilder.CreateTable(
                name: "Departments",
                columns: table => new
                {
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Departments", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_Departments_Organizations_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Organizations",
                        principalColumn: "TenantId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "FxRates",
                columns: table => new
                {
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    EffectiveDate = table.Column<DateOnly>(type: "date", nullable: false),
                    UsdPerUnit = table.Column<decimal>(type: "decimal(19,8)", precision: 19, scale: 8, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FxRates", x => new { x.TenantId, x.Id });
                    table.CheckConstraint("CK_FxRates_Positive", "[UsdPerUnit] > 0");
                    table.ForeignKey(
                        name: "FK_FxRates_Organizations_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Organizations",
                        principalColumn: "TenantId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "JobTemplates",
                columns: table => new
                {
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JobTemplates", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_JobTemplates_Organizations_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Organizations",
                        principalColumn: "TenantId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Contracts",
                columns: table => new
                {
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DepartmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Domain = table.Column<string>(type: "nvarchar(253)", maxLength: 253, nullable: false),
                    Category = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    Cost = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    Currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    Cycle = table.Column<string>(type: "nvarchar(12)", maxLength: 12, nullable: false),
                    Seats = table.Column<int>(type: "int", nullable: false),
                    NextPayment = table.Column<DateOnly>(type: "date", nullable: false),
                    NoticeDays = table.Column<int>(type: "int", nullable: false),
                    AutoRenew = table.Column<bool>(type: "bit", nullable: false),
                    Active = table.Column<bool>(type: "bit", nullable: false),
                    ChangedBy = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    Version = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    PeriodEnd = table.Column<DateTime>(type: "datetime2", nullable: false)
                        .Annotation("SqlServer:TemporalIsPeriodEndColumn", true),
                    PeriodStart = table.Column<DateTime>(type: "datetime2", nullable: false)
                        .Annotation("SqlServer:TemporalIsPeriodStartColumn", true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Contracts", x => new { x.TenantId, x.Id });
                    table.CheckConstraint("CK_Contracts_Values", "[Cost] >= 0 AND [Seats] >= 0 AND [NoticeDays] BETWEEN 0 AND 365");
                    table.ForeignKey(
                        name: "FK_Contracts_Departments_TenantId_DepartmentId",
                        columns: x => new { x.TenantId, x.DepartmentId },
                        principalTable: "Departments",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Contracts_Organizations_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Organizations",
                        principalColumn: "TenantId",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("SqlServer:IsTemporal", true)
                .Annotation("SqlServer:TemporalHistoryTableName", "ContractHistory")
                .Annotation("SqlServer:TemporalHistoryTableSchema", null)
                .Annotation("SqlServer:TemporalPeriodEndColumnName", "PeriodEnd")
                .Annotation("SqlServer:TemporalPeriodStartColumnName", "PeriodStart");

            migrationBuilder.CreateTable(
                name: "Employees",
                columns: table => new
                {
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DepartmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Email = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    Job = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Version = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Employees", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_Employees_Departments_TenantId_DepartmentId",
                        columns: x => new { x.TenantId, x.DepartmentId },
                        principalTable: "Departments",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Employees_Organizations_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Organizations",
                        principalColumn: "TenantId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PurchaseRequests",
                columns: table => new
                {
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DepartmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Domain = table.Column<string>(type: "nvarchar(253)", maxLength: 253, nullable: false),
                    Category = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(600)", maxLength: 600, nullable: false),
                    MonthlyCost = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    Currency = table.Column<int>(type: "int", nullable: false),
                    State = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    Version = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PurchaseRequests", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_PurchaseRequests_Departments_TenantId_DepartmentId",
                        columns: x => new { x.TenantId, x.DepartmentId },
                        principalTable: "Departments",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PurchaseRequests_Organizations_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Organizations",
                        principalColumn: "TenantId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RadarAlerts",
                columns: table => new
                {
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ContractId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    DueOn = table.Column<DateOnly>(type: "date", nullable: false),
                    DetectedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RadarAlerts", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_RadarAlerts_Contracts_TenantId_ContractId",
                        columns: x => new { x.TenantId, x.ContractId },
                        principalTable: "Contracts",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RadarAlerts_Organizations_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Organizations",
                        principalColumn: "TenantId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TemplateItems",
                columns: table => new
                {
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    JobTemplateId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ContractId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Role = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TemplateItems", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_TemplateItems_Contracts_TenantId_ContractId",
                        columns: x => new { x.TenantId, x.ContractId },
                        principalTable: "Contracts",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TemplateItems_JobTemplates_TenantId_JobTemplateId",
                        columns: x => new { x.TenantId, x.JobTemplateId },
                        principalTable: "JobTemplates",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TemplateItems_Organizations_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Organizations",
                        principalColumn: "TenantId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AccessGrants",
                columns: table => new
                {
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ContractId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(12)", maxLength: 12, nullable: false),
                    Role = table.Column<string>(type: "nvarchar(12)", maxLength: 12, nullable: false),
                    AssetLabel = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    GrantedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastUsedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RevokedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ChangedBy = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    Version = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    PeriodEnd = table.Column<DateTime>(type: "datetime2", nullable: false)
                        .Annotation("SqlServer:TemporalIsPeriodEndColumn", true),
                    PeriodStart = table.Column<DateTime>(type: "datetime2", nullable: false)
                        .Annotation("SqlServer:TemporalIsPeriodStartColumn", true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccessGrants", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_AccessGrants_Contracts_TenantId_ContractId",
                        columns: x => new { x.TenantId, x.ContractId },
                        principalTable: "Contracts",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AccessGrants_Employees_TenantId_EmployeeId",
                        columns: x => new { x.TenantId, x.EmployeeId },
                        principalTable: "Employees",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AccessGrants_Organizations_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Organizations",
                        principalColumn: "TenantId",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("SqlServer:IsTemporal", true)
                .Annotation("SqlServer:TemporalHistoryTableName", "AccessGrantHistory")
                .Annotation("SqlServer:TemporalHistoryTableSchema", null)
                .Annotation("SqlServer:TemporalPeriodEndColumnName", "PeriodEnd")
                .Annotation("SqlServer:TemporalPeriodStartColumnName", "PeriodStart");

            migrationBuilder.CreateTable(
                name: "AccessRuns",
                columns: table => new
                {
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    StartedBy = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CompletedBy = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    Version = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccessRuns", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_AccessRuns_Employees_TenantId_EmployeeId",
                        columns: x => new { x.TenantId, x.EmployeeId },
                        principalTable: "Employees",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AccessRuns_Organizations_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Organizations",
                        principalColumn: "TenantId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PurchaseDecisions",
                columns: table => new
                {
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PurchaseRequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FromState = table.Column<int>(type: "int", nullable: false),
                    ToState = table.Column<int>(type: "int", nullable: false),
                    Actor = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    Comment = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    DecidedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PurchaseDecisions", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_PurchaseDecisions_Organizations_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Organizations",
                        principalColumn: "TenantId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PurchaseDecisions_PurchaseRequests_TenantId_PurchaseRequestId",
                        columns: x => new { x.TenantId, x.PurchaseRequestId },
                        principalTable: "PurchaseRequests",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RunTasks",
                columns: table => new
                {
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AccessRunId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ContractId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AccessGrantId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    Role = table.Column<int>(type: "int", nullable: false),
                    AssetLabel = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ServiceName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Evidence = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CompletedBy = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    Version = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RunTasks", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_RunTasks_AccessGrants_TenantId_AccessGrantId",
                        columns: x => new { x.TenantId, x.AccessGrantId },
                        principalTable: "AccessGrants",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RunTasks_AccessRuns_TenantId_AccessRunId",
                        columns: x => new { x.TenantId, x.AccessRunId },
                        principalTable: "AccessRuns",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RunTasks_Contracts_TenantId_ContractId",
                        columns: x => new { x.TenantId, x.ContractId },
                        principalTable: "Contracts",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RunTasks_Organizations_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Organizations",
                        principalColumn: "TenantId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AccessGrants_TenantId_ContractId",
                table: "AccessGrants",
                columns: new[] { "TenantId", "ContractId" });

            migrationBuilder.CreateIndex(
                name: "IX_AccessGrants_TenantId_EmployeeId_ContractId_Kind_AssetLabel",
                table: "AccessGrants",
                columns: new[] { "TenantId", "EmployeeId", "ContractId", "Kind", "AssetLabel" },
                unique: true,
                filter: "[RevokedAt] IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AccessRuns_TenantId_EmployeeId",
                table: "AccessRuns",
                columns: new[] { "TenantId", "EmployeeId" },
                unique: true,
                filter: "[CompletedAt] IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AccessRuns_TenantId_EmployeeId_Kind",
                table: "AccessRuns",
                columns: new[] { "TenantId", "EmployeeId", "Kind" },
                unique: true,
                filter: "[Kind] = 1");

            migrationBuilder.CreateIndex(
                name: "IX_Contracts_TenantId_Active_NextPayment",
                table: "Contracts",
                columns: new[] { "TenantId", "Active", "NextPayment" });

            migrationBuilder.CreateIndex(
                name: "IX_Contracts_TenantId_DepartmentId",
                table: "Contracts",
                columns: new[] { "TenantId", "DepartmentId" });

            migrationBuilder.CreateIndex(
                name: "IX_Contracts_TenantId_Domain",
                table: "Contracts",
                columns: new[] { "TenantId", "Domain" },
                unique: true,
                filter: "[Active] = 1");

            migrationBuilder.CreateIndex(
                name: "IX_Departments_TenantId_Name",
                table: "Departments",
                columns: new[] { "TenantId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Employees_TenantId_DepartmentId",
                table: "Employees",
                columns: new[] { "TenantId", "DepartmentId" });

            migrationBuilder.CreateIndex(
                name: "IX_Employees_TenantId_Email",
                table: "Employees",
                columns: new[] { "TenantId", "Email" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FxRates_TenantId_Currency_EffectiveDate",
                table: "FxRates",
                columns: new[] { "TenantId", "Currency", "EffectiveDate" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_JobTemplates_TenantId_Name",
                table: "JobTemplates",
                columns: new[] { "TenantId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseDecisions_TenantId_PurchaseRequestId",
                table: "PurchaseDecisions",
                columns: new[] { "TenantId", "PurchaseRequestId" });

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseRequests_TenantId_DepartmentId",
                table: "PurchaseRequests",
                columns: new[] { "TenantId", "DepartmentId" });

            migrationBuilder.CreateIndex(
                name: "IX_RadarAlerts_TenantId_ContractId_Kind_DueOn",
                table: "RadarAlerts",
                columns: new[] { "TenantId", "ContractId", "Kind", "DueOn" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RunTasks_TenantId_AccessGrantId",
                table: "RunTasks",
                columns: new[] { "TenantId", "AccessGrantId" });

            migrationBuilder.CreateIndex(
                name: "IX_RunTasks_TenantId_AccessRunId",
                table: "RunTasks",
                columns: new[] { "TenantId", "AccessRunId" });

            migrationBuilder.CreateIndex(
                name: "IX_RunTasks_TenantId_ContractId",
                table: "RunTasks",
                columns: new[] { "TenantId", "ContractId" });

            migrationBuilder.CreateIndex(
                name: "IX_TemplateItems_TenantId_ContractId",
                table: "TemplateItems",
                columns: new[] { "TenantId", "ContractId" });

            migrationBuilder.CreateIndex(
                name: "IX_TemplateItems_TenantId_JobTemplateId_ContractId",
                table: "TemplateItems",
                columns: new[] { "TenantId", "JobTemplateId", "ContractId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FxRates");

            migrationBuilder.DropTable(
                name: "PurchaseDecisions");

            migrationBuilder.DropTable(
                name: "RadarAlerts");

            migrationBuilder.DropTable(
                name: "RunTasks");

            migrationBuilder.DropTable(
                name: "TemplateItems");

            migrationBuilder.DropTable(
                name: "PurchaseRequests");

            migrationBuilder.DropTable(
                name: "AccessGrants")
                .Annotation("SqlServer:IsTemporal", true)
                .Annotation("SqlServer:TemporalHistoryTableName", "AccessGrantHistory")
                .Annotation("SqlServer:TemporalHistoryTableSchema", null)
                .Annotation("SqlServer:TemporalPeriodEndColumnName", "PeriodEnd")
                .Annotation("SqlServer:TemporalPeriodStartColumnName", "PeriodStart");

            migrationBuilder.DropTable(
                name: "AccessRuns");

            migrationBuilder.DropTable(
                name: "JobTemplates");

            migrationBuilder.DropTable(
                name: "Contracts")
                .Annotation("SqlServer:IsTemporal", true)
                .Annotation("SqlServer:TemporalHistoryTableName", "ContractHistory")
                .Annotation("SqlServer:TemporalHistoryTableSchema", null)
                .Annotation("SqlServer:TemporalPeriodEndColumnName", "PeriodEnd")
                .Annotation("SqlServer:TemporalPeriodStartColumnName", "PeriodStart");

            migrationBuilder.DropTable(
                name: "Employees");

            migrationBuilder.DropTable(
                name: "Departments");

            migrationBuilder.DropTable(
                name: "Organizations");
        }
    }
}
