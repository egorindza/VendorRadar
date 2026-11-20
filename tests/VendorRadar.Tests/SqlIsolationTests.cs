using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using VendorRadar.Application;
using VendorRadar.Infrastructure;
using Xunit;

namespace VendorRadar.Tests;

public sealed class LocalSqlFactAttribute : FactAttribute
{
    public LocalSqlFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("VENDORRADAR_SQL_TESTS") != "1")
            Skip = "Requires the seeded local VendorRadar SQL Server database.";
    }
}

public sealed class SqlIsolationTests
{
    private static readonly DatabaseSettings Local = new(DatabaseSettings.LocalConnection, true);

    [LocalSqlFact]
    public async Task RlsAppliesWithoutEfFiltersAndToDapperTemporalRows()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var ct = timeout.Token;
        await using var first = WorkspaceDb.Create(Local, new Actor(DemoData.Northstar, "Integration tests", ""));
        await using var second = WorkspaceDb.Create(Local, new Actor(DemoData.Meridian, "Integration tests", ""));
        Assert.Equal(new[] { DemoData.Northstar }, await first.Contracts.IgnoreQueryFilters().Select(x => x.TenantId).Distinct().ToListAsync(ct));
        Assert.Equal(new[] { DemoData.Meridian }, await second.Contracts.IgnoreQueryFilters().Select(x => x.TenantId).Distinct().ToListAsync(ct));
        var firstId = await first.Contracts.Where(x => x.Name == "Slack").Select(x => x.Id).SingleAsync(ct);

        await using var connection = await TenantConnections.OpenAsync(Local, DemoData.Meridian, ct);
        Assert.Equal(0, await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.Contracts FOR SYSTEM_TIME ALL WHERE Id = @firstId", new { firstId }));
        Assert.Equal(0, await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.ContractHistory WHERE Id = @firstId", new { firstId }));
        Assert.Equal(0, await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.AccessGrantHistory WHERE TenantId = @tenant", new { tenant = DemoData.Northstar }));
        var analytics = new Analytics(Local, new FixedActor(new(DemoData.Northstar, "Integration tests", "")), TimeProvider.System);
        Assert.True((await analytics.CostHistoryAsync(firstId, ct)).Count >= 2);
        var snapshot = await analytics.ReadAsync(ct);
        Assert.True(snapshot.Idle > 0);
        Assert.True(snapshot.Mrr > 0);
        Assert.Equal(snapshot.Mrr * 12, snapshot.Arr);
    }

    [LocalSqlFact]
    public async Task RuntimeCannotWriteCrossTenantOrRebindItsScope()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var ct = timeout.Token;
        await using var second = WorkspaceDb.Create(Local, new Actor(DemoData.Meridian, "Integration tests", ""));
        var otherId = await second.Contracts.Select(x => x.Id).FirstAsync(ct);
        await using var connection = await TenantConnections.OpenAsync(Local, DemoData.Northstar, ct);
        var blocked = await Assert.ThrowsAsync<SqlException>(() => connection.ExecuteAsync("""
            INSERT dbo.RadarAlerts (TenantId, Id, ContractId, Kind, DueOn, DetectedAt)
            VALUES (@tenant, NEWID(), @otherId, 0, '2099-12-31', SYSUTCDATETIME());
            """, new { tenant = DemoData.Meridian, otherId }));
        Assert.Equal(33504, blocked.Number);
        await Assert.ThrowsAsync<SqlException>(() => connection.ExecuteAsync(
            "EXEC sys.sp_set_session_context @key=N'TenantId', @value=@tenant;",
            new { tenant = DemoData.Meridian }));
        Assert.Equal("VendorRadar_LocalRuntime", await connection.ExecuteScalarAsync<string>("SELECT USER_NAME()"));
    }

    [LocalSqlFact]
    public async Task AnUnscopedDatabaseConnectionSeesNoTenantRows()
    {
        await using var connection = new SqlConnection(new SqlConnectionStringBuilder(Local.ConnectionString) { Pooling = false }.ConnectionString);
        await connection.OpenAsync();
        Assert.Equal(0, await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.Contracts"));
        Assert.Equal(0, await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.ContractHistory"));
    }
}
