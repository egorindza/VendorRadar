using Dapper;
using VendorRadar.Application;

namespace VendorRadar.Infrastructure;

public sealed class Analytics(DatabaseSettings settings, ICurrentActor actor, TimeProvider clock) : IAnalytics
{
    public async Task<AnalyticsSnapshot> ReadAsync(CancellationToken ct)
    {
        await using var connection = await TenantConnections.OpenAsync(settings, actor.Value.TenantId, ct);
        var parameters = new
        {
            Today = DateOnly.FromDateTime(clock.GetLocalNow().DateTime).ToDateTime(TimeOnly.MinValue),
            IdleBefore = clock.GetUtcNow().UtcDateTime.AddDays(-30)
        };
        using var rows = await connection.QueryMultipleAsync(new CommandDefinition(Spend + Seats + Departments, parameters, cancellationToken: ct));
        return new AnalyticsSnapshot(
            (await rows.ReadAsync<SpendRow>()).AsList(),
            (await rows.ReadAsync<SeatRow>()).AsList(),
            (await rows.ReadAsync<DepartmentSpend>()).AsList());
    }

    public async Task<IReadOnlyList<CostVersion>> CostHistoryAsync(Guid id, CancellationToken ct)
    {
        await using var connection = await TenantConnections.OpenAsync(settings, actor.Value.TenantId, ct);
        const string sql = """
            WITH Versions AS (
                SELECT Cost, Currency, Cycle, ChangedBy, PeriodStart AS ValidFrom, PeriodEnd AS ValidTo,
                       LAG(Cost) OVER (ORDER BY PeriodStart, PeriodEnd) AS PriorCost,
                       LAG(Currency) OVER (ORDER BY PeriodStart, PeriodEnd) AS PriorCurrency,
                       LAG(Cycle) OVER (ORDER BY PeriodStart, PeriodEnd) AS PriorCycle
                  FROM dbo.Contracts FOR SYSTEM_TIME ALL WHERE Id = @Id
            )
            SELECT Cost, Currency, Cycle, ChangedBy, ValidFrom, ValidTo FROM Versions
             WHERE PriorCost IS NULL OR PriorCost <> Cost OR PriorCurrency <> Currency OR PriorCycle <> Cycle
             ORDER BY ValidFrom DESC;
            """;
        return (await connection.QueryAsync<CostVersion>(new CommandDefinition(sql, new { id }, cancellationToken: ct))).AsList();
    }

    public async Task<IReadOnlyList<GrantVersion>> GrantHistoryAsync(Guid employeeId, CancellationToken ct)
    {
        await using var connection = await TenantConnections.OpenAsync(settings, actor.Value.TenantId, ct);
        const string sql = """
            WITH Versions AS (
                SELECT a.TenantId, a.Id, a.ContractId, a.Kind, a.Role, a.AssetLabel, a.RevokedAt, a.ChangedBy,
                       a.PeriodStart AS ValidFrom, a.PeriodEnd AS ValidTo,
                       LAG(a.Role) OVER (PARTITION BY a.TenantId, a.Id ORDER BY a.PeriodStart, a.PeriodEnd) AS PriorRole,
                       LAG(a.RevokedAt) OVER (PARTITION BY a.TenantId, a.Id ORDER BY a.PeriodStart, a.PeriodEnd) AS PriorRevoke
                  FROM dbo.AccessGrants FOR SYSTEM_TIME ALL AS a WHERE a.EmployeeId = @EmployeeId
            )
            SELECT c.Name, v.Kind, v.Role, v.AssetLabel, v.RevokedAt, v.ChangedBy, v.ValidFrom, v.ValidTo
              FROM Versions v JOIN dbo.Contracts c ON c.TenantId = v.TenantId AND c.Id = v.ContractId
             WHERE PriorRole IS NULL OR PriorRole <> v.Role OR (PriorRevoke IS NULL AND v.RevokedAt IS NOT NULL)
             ORDER BY ValidFrom DESC;
            """;
        return (await connection.QueryAsync<GrantVersion>(new CommandDefinition(sql, new { employeeId }, cancellationToken: ct))).AsList();
    }

    private const string Normalized = """
        WITH Rates AS (
            SELECT TenantId, Currency, UsdPerUnit,
                   ROW_NUMBER() OVER (PARTITION BY TenantId, Currency ORDER BY EffectiveDate DESC) AS Recency
              FROM dbo.FxRates WHERE EffectiveDate <= @Today
        ), Monthly AS (
            SELECT c.Id, c.TenantId, c.Name, d.Name AS Department,
                   CONVERT(decimal(19,4), c.Cost * CASE WHEN c.Currency = 'USD' THEN 1 ELSE r.UsdPerUnit END /
                      CASE c.Cycle WHEN 'Annual' THEN 12 WHEN 'Quarterly' THEN 3 ELSE 1 END) AS MonthlyUsd
              FROM dbo.Contracts c
              JOIN dbo.Departments d ON d.TenantId = c.TenantId AND d.Id = c.DepartmentId
              LEFT JOIN Rates r ON r.TenantId = c.TenantId AND r.Currency = c.Currency AND r.Recency = 1
             WHERE c.Active = 1
        )
        """;
    private const string Spend = Normalized + """
        SELECT Id, Name, Department, MonthlyUsd,
               SUM(MonthlyUsd) OVER (PARTITION BY TenantId ORDER BY MonthlyUsd DESC, Id ROWS UNBOUNDED PRECEDING) AS RunningMonthlyUsd
          FROM Monthly ORDER BY MonthlyUsd DESC, Id;
        """;
    private const string Seats = """
        WITH Grants AS (
            SELECT DISTINCT a.TenantId, a.ContractId,
                   COUNT(*) OVER (PARTITION BY a.TenantId, a.ContractId) AS Assigned,
                   SUM(CASE WHEN COALESCE(a.LastUsedAt, a.GrantedAt) <= @IdleBefore THEN 1 ELSE 0 END)
                       OVER (PARTITION BY a.TenantId, a.ContractId) AS Idle
              FROM dbo.AccessGrants a WHERE a.Kind = 'Seat' AND a.RevokedAt IS NULL
        )
        SELECT c.Id AS ContractId, c.Name, c.Seats AS Paid, COALESCE(g.Assigned, 0) AS Assigned, COALESCE(g.Idle, 0) AS Idle,
               CASE WHEN c.Seats > COALESCE(g.Assigned, 0) THEN c.Seats - COALESCE(g.Assigned, 0) ELSE 0 END AS Free
          FROM dbo.Contracts c LEFT JOIN Grants g ON g.TenantId = c.TenantId AND g.ContractId = c.Id
         WHERE c.Active = 1 ORDER BY Idle DESC, c.Name;
        """;
    private const string Departments = Normalized + """
        SELECT Department, COALESCE(SUM(MonthlyUsd), 0) AS MonthlyUsd, COUNT(*) AS Contracts
          FROM Monthly GROUP BY Department ORDER BY MonthlyUsd DESC, Department;
        """;
}
