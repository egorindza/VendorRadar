using System.Data;
using System.Data.Common;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Diagnostics;
using VendorRadar.Application;

namespace VendorRadar.Infrastructure;

public sealed record DatabaseSettings(string ConnectionString, bool ImpersonateLocalUser)
{
    public const string LocalConnection = "Server=lpc:localhost;Database=VendorRadar_SaasDb;Integrated Security=True;Encrypt=False;TrustServerCertificate=True;Application Name=VendorRadar";

    public string ScopedConnectionString => new SqlConnectionStringBuilder(ConnectionString)
    {
        Pooling = !ImpersonateLocalUser,
        MultipleActiveResultSets = false
    }.ConnectionString;
}

public static class TenantConnections
{
    public static async Task<SqlConnection> OpenAsync(DatabaseSettings settings, Guid tenant, CancellationToken ct)
    {
        var connection = new SqlConnection(settings.ScopedConnectionString);
        try
        {
            await connection.OpenAsync(ct);
            await BindAsync(connection, settings, tenant, ct);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    internal static void Bind(DbConnection connection, DatabaseSettings settings, Guid tenant)
    {
        if (settings.ImpersonateLocalUser)
        {
            using var impersonate = ImpersonationCommand(connection);
            impersonate.ExecuteNonQuery();
        }
        using var command = SessionCommand(connection, tenant);
        command.ExecuteNonQuery();
    }

    internal static async Task BindAsync(DbConnection connection, DatabaseSettings settings, Guid tenant, CancellationToken ct)
    {
        if (settings.ImpersonateLocalUser)
        {
            await using var impersonate = ImpersonationCommand(connection);
            await impersonate.ExecuteNonQueryAsync(ct);
        }
        await using var command = SessionCommand(connection, tenant);
        await command.ExecuteNonQueryAsync(ct);
    }

    private static DbCommand ImpersonationCommand(DbConnection connection)
    {
        // NO REVERT requires an ad hoc batch. SqlClient wraps parameterized SQL in sp_executesql.
        var command = connection.CreateCommand();
        command.CommandText = "EXECUTE AS USER = 'VendorRadar_LocalRuntime' WITH NO REVERT;";
        return command;
    }

    private static DbCommand SessionCommand(DbConnection connection, Guid tenant)
    {
        if (tenant == Guid.Empty) throw new InvalidOperationException("Tenant scope is required.");
        var command = connection.CreateCommand();
        command.CommandText = "EXEC sys.sp_set_session_context @key=N'TenantId', @value=@tenant, @read_only=1;";
        var parameter = command.CreateParameter();
        parameter.ParameterName = "@tenant";
        parameter.DbType = DbType.Guid;
        parameter.Value = tenant;
        command.Parameters.Add(parameter);
        return command;
    }
}

public sealed class TenantSessionInterceptor(DatabaseSettings settings, ICurrentActor actor) : DbConnectionInterceptor
{
    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData) =>
        TenantConnections.Bind(connection, settings, actor.Value.TenantId);

    public override Task ConnectionOpenedAsync(DbConnection connection, ConnectionEndEventData eventData, CancellationToken cancellationToken = default) =>
        TenantConnections.BindAsync(connection, settings, actor.Value.TenantId, cancellationToken);
}

public sealed class DesignTimeRadarDb : IDesignTimeDbContextFactory<RadarDb>
{
    public RadarDb CreateDbContext(string[] args) => new(
        new DbContextOptionsBuilder<RadarDb>().UseSqlServer(DatabaseSettings.LocalConnection).Options,
        new SchemaActor());

    private sealed class SchemaActor : ICurrentActor
    {
        public Actor Value => throw new InvalidOperationException("Schema operations do not use tenant runtime data.");
    }
}
