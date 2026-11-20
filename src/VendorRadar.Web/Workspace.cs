using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using VendorRadar.Application;
using VendorRadar.Domain;
using VendorRadar.Infrastructure;

namespace VendorRadar.Web;

public sealed class WorkspaceDirectory
{
    public sealed record Account(string Key, Actor Actor, string? Hash);
    public bool DemoEnabled { get; }
    public IReadOnlyList<Account> Accounts { get; }
    public IReadOnlyList<Guid> TenantIds { get; }

    public WorkspaceDirectory(IConfiguration configuration, bool development)
    {
        DemoEnabled = development && configuration.GetValue<bool>("Demo:Enabled");
        var accounts = new List<Account>();
        foreach (var row in configuration.GetSection("Accounts").GetChildren())
        {
            var tenant = row.GetValue<Guid>("TenantId");
            var role = row["Role"];
            var department = row.GetValue<Guid?>("DepartmentId");
            var name = row["Name"];
            var hash = row["PasswordHash"];
            if (tenant == Guid.Empty || role is not (Roles.Finance or Roles.Security or Roles.Lead) ||
                (role == Roles.Lead && (department is null || department == Guid.Empty)) ||
                string.IsNullOrWhiteSpace(name) || name.Length > 120 || string.IsNullOrWhiteSpace(hash) ||
                row.Key.StartsWith("demo-", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Invalid account configuration: {row.Key}");
            accounts.Add(new(row.Key, new(tenant, name, role, department), hash));
        }
        if (DemoEnabled)
        {
            foreach (var (tenant, _) in DemoData.Tenants)
            {
                var prefix = tenant == DemoData.Northstar ? "demo-northstar-" : "demo-meridian-";
                accounts.Add(new(prefix + "finance", new(tenant, "CEO / FinOps", Roles.Finance), null));
                accounts.Add(new(prefix + "security", new(tenant, "IT Security Admin", Roles.Security), null));
                accounts.Add(new(prefix + "lead", new(tenant, "Team Lead", Roles.Lead, DemoData.Engineering), null));
            }
        }
        Accounts = accounts;
        TenantIds = accounts.Select(x => x.Actor.TenantId).Distinct().ToArray();
    }

    public Account? Find(string? key) => Accounts.SingleOrDefault(x => string.Equals(x.Key, key, StringComparison.OrdinalIgnoreCase));

    public bool AllowDemo(HttpContext context) =>
        DemoEnabled && context.Connection.RemoteIpAddress is { } address && IPAddress.IsLoopback(address);
}

public sealed class HttpActor(IHttpContextAccessor accessor) : ICurrentActor
{
    public Actor Value => From(accessor.HttpContext?.User);

    public static Actor From(ClaimsPrincipal? principal)
    {
        if (principal?.Identity?.IsAuthenticated != true ||
            !Guid.TryParse(principal.FindFirstValue("tenant"), out var tenant) || tenant == Guid.Empty)
            throw new OperationDeniedException();
        var department = Guid.TryParse(principal.FindFirstValue("department"), out var value) ? value : (Guid?)null;
        return new(tenant, principal.Identity.Name!, principal.FindFirstValue(ClaimTypes.Role)!, department);
    }
}

public static class CookieSessions
{
    public static ClaimsPrincipal Principal(WorkspaceDirectory.Account account)
    {
        var actor = account.Actor;
        return new(new ClaimsIdentity(
        [
            new(ClaimTypes.NameIdentifier, account.Key),
            new(ClaimTypes.Name, actor.Name),
            new(ClaimTypes.Role, actor.Role),
            new("tenant", actor.TenantId.ToString()),
            new("department", actor.DepartmentId?.ToString() ?? "")
        ], CookieAuthenticationDefaults.AuthenticationScheme));
    }

    public static async Task ValidateAsync(CookieValidatePrincipalContext context)
    {
        var directory = context.HttpContext.RequestServices.GetRequiredService<WorkspaceDirectory>();
        var account = directory.Find(context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier));
        if (account is not null && (account.Hash is not null || directory.AllowDemo(context.HttpContext)) &&
            HttpActor.From(context.Principal) == account.Actor) return;
        context.RejectPrincipal();
        await context.HttpContext.SignOutAsync();
    }

    public static Task ChallengeAsync(RedirectContext<CookieAuthenticationOptions> context)
    {
        if (context.Request.Path.StartsWithSegments("/realtime"))
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        else if (context.Request.Headers.ContainsKey("HX-Request"))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            context.Response.Headers["HX-Redirect"] = context.RedirectUri;
        }
        else context.Response.Redirect(context.RedirectUri);
        return Task.CompletedTask;
    }
}

public sealed class WorkspaceHub : Hub
{
    public override async Task OnConnectedAsync()
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, SignalEvents.Group(HttpActor.From(Context.User).TenantId));
        await base.OnConnectedAsync();
    }
}

public sealed class SignalEvents(IHubContext<WorkspaceHub> hub, ILogger<SignalEvents> log) : IWorkspaceEvents
{
    public static string Group(Guid tenant) => $"workspace:{tenant:N}";

    public async Task PublishAsync(Guid tenant, CancellationToken ct)
    {
        try { await hub.Clients.Group(Group(tenant)).SendAsync("WorkspaceChanged", ct); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.LogWarning(ex, "Workspace notification could not be delivered for {TenantId}", tenant);
        }
    }
}

public sealed class RadarWorker(
    DatabaseSettings database, WorkspaceDirectory directory, TimeProvider clock, IConfiguration settings,
    IWorkspaceEvents events, ILogger<RadarWorker> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = Math.Clamp(settings.GetValue("Radar:IntervalMinutes", 5), 1, 120);
        var horizon = Math.Clamp(settings.GetValue("Radar:HorizonDays", 30), 1, 90);
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(interval), clock);
        do
        {
            foreach (var tenant in directory.TenantIds)
            {
                try
                {
                    await using var db = WorkspaceDb.Create(database, new Actor(tenant, "Contract Radar", ""));
                    if (await ContractRadar.ScanAsync(db, clock, horizon, stoppingToken) > 0)
                        await events.PublishAsync(tenant, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
                catch (DbUpdateException ex) when (ex.InnerException is Microsoft.Data.SqlClient.SqlException { Number: 2601 or 2627 })
                {
                    log.LogDebug("Another worker stored alerts for {TenantId}", tenant);
                }
                catch (Exception ex) { log.LogError(ex, "Contract radar failed for {TenantId}", tenant); }
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
