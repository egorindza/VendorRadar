using VendorRadar.Domain;

namespace VendorRadar.Application;

public static class Roles
{
    public const string Finance = "Finance";
    public const string Security = "Security";
    public const string Lead = "Lead";
}

public sealed record Actor(Guid TenantId, string Name, string Role, Guid? DepartmentId = null);

public interface ICurrentActor
{
    Actor Value { get; }
}

public interface IWorkspaceEvents
{
    Task PublishAsync(Guid tenant, CancellationToken ct);
}

public interface IAnalytics
{
    Task<AnalyticsSnapshot> ReadAsync(CancellationToken ct);
    Task<IReadOnlyList<CostVersion>> CostHistoryAsync(Guid id, CancellationToken ct);
    Task<IReadOnlyList<GrantVersion>> GrantHistoryAsync(Guid employeeId, CancellationToken ct);
}

public sealed record AnalyticsSnapshot(IReadOnlyList<SpendRow> Spend, IReadOnlyList<SeatRow> Seats, IReadOnlyList<DepartmentSpend> Departments)
{
    public decimal Mrr => Spend.Sum(x => x.MonthlyUsd ?? 0);
    public decimal Arr => Mrr * 12;
    public int Idle => Seats.Sum(x => x.Idle);
    public int Free => Seats.Sum(x => x.Free);
    public int Unpriced => Spend.Count(x => x.MonthlyUsd is null);
}

public sealed record SpendRow(Guid Id, string Name, string Department, decimal? MonthlyUsd, decimal? RunningMonthlyUsd);
public sealed record SeatRow(Guid ContractId, string Name, int Paid, int Assigned, int Idle, int Free);
public sealed record DepartmentSpend(string Department, decimal MonthlyUsd, int Contracts);
public sealed record CostVersion(decimal Cost, string Currency, string Cycle, string ChangedBy, DateTime ValidFrom, DateTime ValidTo);
public sealed record GrantVersion(string Name, string Kind, string Role, string AssetLabel, DateTime? RevokedAt, string ChangedBy, DateTime ValidFrom, DateTime ValidTo);
