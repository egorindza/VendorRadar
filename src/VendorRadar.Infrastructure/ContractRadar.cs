using Microsoft.EntityFrameworkCore;
using VendorRadar.Domain;

namespace VendorRadar.Infrastructure;

public sealed record Upcoming(Guid ContractId, string Name, string Category, RadarKind Kind, DateOnly Date, decimal Cost, Currency Currency);

public static class ContractRadar
{
    public static IEnumerable<Upcoming> UpcomingFor(IEnumerable<Contract> contracts, DateOnly today, int days)
    {
        var horizon = today.AddDays(days);
        foreach (var contract in contracts.Where(x => x.Active))
        {
            foreach (var date in BusinessRules.Payments(contract, today, horizon.AddDays(contract.AutoRenew ? contract.NoticeDays : 0)))
            {
                if (date <= horizon)
                    yield return new(contract.Id, contract.Name, contract.Category, RadarKind.Payment, date, contract.Cost, contract.Currency);
                var deadline = date.AddDays(-contract.NoticeDays);
                if (contract.AutoRenew && deadline <= horizon)
                    yield return new(contract.Id, contract.Name, contract.Category, RadarKind.Notice, deadline, contract.Cost, contract.Currency);
            }
        }
    }

    public static async Task<int> ScanAsync(RadarDb db, TimeProvider clock, int days, CancellationToken ct)
    {
        var now = clock.GetLocalNow();
        var today = DateOnly.FromDateTime(now.DateTime);
        var upcoming = UpcomingFor(await db.Contracts.AsNoTracking().Where(x => x.Active).ToListAsync(ct), today, days).ToArray();
        var known = await db.RadarAlerts.Where(x => x.DueOn >= today.AddDays(-365) && x.DueOn <= today.AddDays(days))
            .Select(x => new { x.ContractId, x.Kind, x.DueOn }).ToListAsync(ct);
        var seen = known.Select(x => (x.ContractId, x.Kind, x.DueOn)).ToHashSet();
        var added = upcoming.Where(x => seen.Add((x.ContractId, x.Kind, x.Date))).ToArray();
        if (added.Length == 0) return 0;
        var tenant = await db.Organizations.Select(x => x.Id).SingleAsync(ct);
        db.RadarAlerts.AddRange(added.Select(x => new RadarAlert
        {
            TenantId = tenant, ContractId = x.ContractId, Kind = x.Kind, DueOn = x.Date, DetectedAt = now.UtcDateTime
        }));
        await db.SaveChangesAsync(ct);
        return added.Length;
    }
}
