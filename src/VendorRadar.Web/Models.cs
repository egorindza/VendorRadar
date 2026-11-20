using System.ComponentModel.DataAnnotations;
using VendorRadar.Application;
using VendorRadar.Domain;
using VendorRadar.Infrastructure;

namespace VendorRadar.Web;

public sealed class ContractForm
{
    public Guid? Id { get; set; }
    public string? Version { get; set; }
    [Required, StringLength(100)] public string Name { get; set; } = "";
    [Required, StringLength(253)] public string Domain { get; set; } = "";
    [Required, StringLength(80)] public string Category { get; set; } = "";
    public Guid DepartmentId { get; set; }
    [Range(0, 100000000)] public decimal Cost { get; set; }
    [EnumDataType(typeof(Currency))] public Currency Currency { get; set; }
    [EnumDataType(typeof(BillingCycle))] public BillingCycle Cycle { get; set; } = BillingCycle.Monthly;
    [Range(0, 100000)] public int Seats { get; set; }
    [DataType(DataType.Date)] public DateOnly NextPayment { get; set; }
    [Range(0, 365)] public int NoticeDays { get; set; } = 30;
    public bool AutoRenew { get; set; } = true;
    public bool Active { get; set; } = true;

    public Contract ToContract() => new()
    {
        Name = Name, Domain = Domain, Category = Category, DepartmentId = DepartmentId, Cost = Cost,
        Currency = Currency, Cycle = Cycle, Seats = Seats, NextPayment = NextPayment,
        NoticeDays = NoticeDays, AutoRenew = AutoRenew, Active = Active
    };

    public static ContractForm From(Contract item) => new()
    {
        Id = item.Id, Version = Convert.ToBase64String(item.Version), Name = item.Name, Domain = item.Domain,
        Category = item.Category, DepartmentId = item.DepartmentId, Cost = item.Cost, Currency = item.Currency,
        Cycle = item.Cycle, Seats = item.Seats, NextPayment = item.NextPayment, NoticeDays = item.NoticeDays,
        AutoRenew = item.AutoRenew, Active = item.Active
    };
}

public sealed class HireForm
{
    [Required, StringLength(100)] public string Name { get; set; } = "";
    [Required, EmailAddress, StringLength(160)] public string Email { get; set; } = "";
    public Guid DepartmentId { get; set; }
    public Guid TemplateId { get; set; }
}

public sealed class GrantForm
{
    public Guid EmployeeId { get; set; }
    public Guid ContractId { get; set; }
    [EnumDataType(typeof(AccessKind))] public AccessKind Kind { get; set; }
    [EnumDataType(typeof(AccessRole))] public AccessRole Role { get; set; }
    [StringLength(100)] public string? Label { get; set; }
}

public sealed class PurchaseForm
{
    [Required, StringLength(100)] public string Name { get; set; } = "";
    [Required, StringLength(253)] public string Domain { get; set; } = "";
    [Required, StringLength(80)] public string Category { get; set; } = "";
    [Required, StringLength(600)] public string Reason { get; set; } = "";
    [Range(0.01, 100000000)] public decimal MonthlyCost { get; set; }
    [EnumDataType(typeof(Currency))] public Currency Currency { get; set; }
    public Guid DepartmentId { get; set; }
}

public sealed record Overview(Organization Organization, AnalyticsSnapshot Snapshot, IReadOnlyList<Upcoming> Notices,
    IReadOnlyList<Upcoming> Payments, IReadOnlyList<AccessRun> ActiveRuns, int Purchases, DateOnly Today);

public sealed record ContractDetails(Contract Item, SeatRow? Seats, IReadOnlyList<CostVersion> History);
public sealed record Matrix(IReadOnlyList<Employee> Employees, IReadOnlyList<Contract> Contracts,
    IReadOnlyList<AccessGrant> Grants, IReadOnlyList<SeatRow> Seats);
public sealed record EmployeeDetails(Employee Employee, IReadOnlyList<AccessGrant> Grants, IReadOnlyList<AccessRun> Runs,
    IReadOnlyList<GrantVersion> History, IReadOnlyList<Contract> Services);
public sealed record PurchaseDetails(PurchaseRequest Item, IReadOnlyList<Contract> Duplicates, bool CanDecide);

public static class Labels
{
    public static string Role(AccessRole role) => role.ToString();
    public static string Cycle(BillingCycle cycle) => cycle switch
    {
        BillingCycle.Monthly => "Ежемесячно", BillingCycle.Quarterly => "Ежеквартально", _ => "Ежегодно"
    };

    public static string Person(EmployeeStatus status) => status switch
    {
        EmployeeStatus.Active => "Активен", EmployeeStatus.Onboarding => "На онбординге",
        EmployeeStatus.Offboarding => "На оффбординге", _ => "Уволен"
    };

    public static string Purchase(PurchaseState state) => state switch
    {
        PurchaseState.DepartmentReview => "Согласование отдела", PurchaseState.FinanceReview => "У финдиректора",
        PurchaseState.Approved => "Одобрена", _ => "Отклонена"
    };

    public static string Run(RunKind kind) => kind == RunKind.Onboarding ? "Онбординг" : "Оффбординг";
    public static string Money(decimal cost) => "$" + cost.ToString("N0", System.Globalization.CultureInfo.GetCultureInfo("en-US"));
}
