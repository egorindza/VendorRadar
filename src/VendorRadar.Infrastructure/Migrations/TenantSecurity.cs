using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace VendorRadar.Infrastructure.Migrations;

[DbContext(typeof(RadarDb))]
[Migration("20261120163000_TenantSecurity")]
public sealed class TenantSecurity : Migration
{
    private static readonly string[] Tables =
    [
        "Organizations", "Departments", "Contracts", "Employees", "AccessGrants", "JobTemplates",
        "TemplateItems", "AccessRuns", "RunTasks", "PurchaseRequests", "PurchaseDecisions", "FxRates", "RadarAlerts"
    ];

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("CREATE SCHEMA [Security] AUTHORIZATION [dbo];");
        migrationBuilder.Sql("""
            EXEC(N'CREATE FUNCTION [Security].[MatchesTenant](@TenantId uniqueidentifier)
            RETURNS TABLE WITH SCHEMABINDING
            AS RETURN SELECT 1 AS Allowed
            WHERE @TenantId = TRY_CONVERT(uniqueidentifier, SESSION_CONTEXT(N''TenantId''));');
            """);
        var predicates = Tables.SelectMany(table => new[]
        {
            $"ADD FILTER PREDICATE [Security].[MatchesTenant]([TenantId]) ON [dbo].[{table}]",
            $"ADD BLOCK PREDICATE [Security].[MatchesTenant]([TenantId]) ON [dbo].[{table}] AFTER INSERT",
            $"ADD BLOCK PREDICATE [Security].[MatchesTenant]([TenantId]) ON [dbo].[{table}] AFTER UPDATE"
        }).Concat(new[]
        {
            "ADD FILTER PREDICATE [Security].[MatchesTenant]([TenantId]) ON [dbo].[ContractHistory]",
            "ADD FILTER PREDICATE [Security].[MatchesTenant]([TenantId]) ON [dbo].[AccessGrantHistory]"
        });
        migrationBuilder.Sql("CREATE SECURITY POLICY [Security].[TenantPolicy] " +
            string.Join(",\n", predicates) + " WITH (STATE = ON, SCHEMABINDING = ON);");
        migrationBuilder.Sql("""
            CREATE ROLE [VendorRadar_Runtime] AUTHORIZATION [dbo];
            CREATE USER [VendorRadar_LocalRuntime] WITHOUT LOGIN;
            ALTER ROLE [VendorRadar_Runtime] ADD MEMBER [VendorRadar_LocalRuntime];
            """);
        foreach (var table in Tables.Concat(["ContractHistory", "AccessGrantHistory"]))
            migrationBuilder.Sql($"GRANT SELECT ON [dbo].[{table}] TO [VendorRadar_Runtime];");
        foreach (var table in new[] { "Contracts", "Employees", "AccessGrants", "AccessRuns", "RunTasks", "PurchaseRequests" })
            migrationBuilder.Sql($"GRANT INSERT, UPDATE ON [dbo].[{table}] TO [VendorRadar_Runtime];");
        foreach (var table in new[] { "JobTemplates", "TemplateItems", "PurchaseDecisions", "RadarAlerts", "FxRates" })
            migrationBuilder.Sql($"GRANT INSERT ON [dbo].[{table}] TO [VendorRadar_Runtime];");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DROP SECURITY POLICY [Security].[TenantPolicy];
            DROP FUNCTION [Security].[MatchesTenant];
            DROP SCHEMA [Security];
            ALTER ROLE [VendorRadar_Runtime] DROP MEMBER [VendorRadar_LocalRuntime];
            DROP USER [VendorRadar_LocalRuntime];
            DROP ROLE [VendorRadar_Runtime];
            """);
    }
}
