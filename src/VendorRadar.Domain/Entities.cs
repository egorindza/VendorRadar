using System.ComponentModel.DataAnnotations;

namespace VendorRadar.Domain;

public enum BillingCycle { Monthly = 1, Quarterly = 3, Annual = 12 }
public enum Currency { USD, EUR, GBP }
public enum AccessRole { Member, Admin, Owner }
public enum AccessKind { Seat, ApiKey }
public enum EmployeeStatus { Active, Onboarding, Offboarding, Departed }
public enum RunKind { Onboarding, Offboarding }
public enum PurchaseState { DepartmentReview, FinanceReview, Approved, Rejected }
public enum RadarKind { Notice, Payment }

public abstract class TenantRow
{
    public Guid TenantId { get; set; }
    public Guid Id { get; set; } = Guid.NewGuid();
}

public sealed class Organization : TenantRow
{
    [MaxLength(100)] public string Name { get; set; } = "";
}

public sealed class Department : TenantRow
{
    [MaxLength(100)] public string Name { get; set; } = "";
}

public sealed class Contract : TenantRow
{
    public Guid DepartmentId { get; set; }
    public Department Department { get; set; } = null!;
    [MaxLength(100)] public string Name { get; set; } = "";
    [MaxLength(253)] public string Domain { get; set; } = "";
    [MaxLength(80)] public string Category { get; set; } = "";
    public decimal Cost { get; set; }
    public Currency Currency { get; set; }
    public BillingCycle Cycle { get; set; }
    public int Seats { get; set; }
    public DateOnly NextPayment { get; set; }
    public int NoticeDays { get; set; }
    public bool AutoRenew { get; set; }
    public bool Active { get; set; } = true;
    [MaxLength(120)] public string ChangedBy { get; set; } = "";
    [Timestamp] public byte[] Version { get; set; } = [];

    public DateOnly NoticeDeadline => NextPayment.AddDays(-NoticeDays);
    public decimal Monthly => Cost / (int)Cycle;
}

public sealed class Employee : TenantRow
{
    public Guid DepartmentId { get; set; }
    public Department Department { get; set; } = null!;
    [MaxLength(100)] public string Name { get; set; } = "";
    [MaxLength(160)] public string Email { get; set; } = "";
    [MaxLength(100)] public string Job { get; set; } = "";
    public EmployeeStatus Status { get; set; }
    [Timestamp] public byte[] Version { get; set; } = [];
}

public sealed class AccessGrant : TenantRow
{
    public Guid EmployeeId { get; set; }
    public Employee Employee { get; set; } = null!;
    public Guid ContractId { get; set; }
    public Contract Contract { get; set; } = null!;
    public AccessKind Kind { get; set; }
    public AccessRole Role { get; set; }
    [MaxLength(100)] public string AssetLabel { get; set; } = "";
    public DateTime GrantedAt { get; set; }
    public DateTime? LastUsedAt { get; set; }
    public DateTime? RevokedAt { get; set; }
    [MaxLength(120)] public string ChangedBy { get; set; } = "";
    [Timestamp] public byte[] Version { get; set; } = [];
}

public sealed class JobTemplate : TenantRow
{
    [MaxLength(100)] public string Name { get; set; } = "";
    public List<TemplateItem> Items { get; set; } = [];
}

public sealed class TemplateItem : TenantRow
{
    public Guid JobTemplateId { get; set; }
    public JobTemplate JobTemplate { get; set; } = null!;
    public Guid ContractId { get; set; }
    public Contract Contract { get; set; } = null!;
    public AccessRole Role { get; set; }
}

public sealed class AccessRun : TenantRow
{
    public Guid EmployeeId { get; set; }
    public Employee Employee { get; set; } = null!;
    public RunKind Kind { get; set; }
    public DateTime StartedAt { get; set; }
    [MaxLength(120)] public string StartedBy { get; set; } = "";
    public DateTime? CompletedAt { get; set; }
    [MaxLength(120)] public string? CompletedBy { get; set; }
    [Timestamp] public byte[] Version { get; set; } = [];
    public List<RunTask> Tasks { get; set; } = [];
}

public sealed class RunTask : TenantRow
{
    public Guid AccessRunId { get; set; }
    public AccessRun AccessRun { get; set; } = null!;
    public Guid ContractId { get; set; }
    public Contract Contract { get; set; } = null!;
    public Guid? AccessGrantId { get; set; }
    public AccessGrant? AccessGrant { get; set; }
    public AccessKind Kind { get; set; }
    public AccessRole Role { get; set; }
    [MaxLength(100)] public string AssetLabel { get; set; } = "";
    [MaxLength(100)] public string ServiceName { get; set; } = "";
    [MaxLength(400)] public string? Evidence { get; set; }
    public DateTime? CompletedAt { get; set; }
    [MaxLength(120)] public string? CompletedBy { get; set; }
    [Timestamp] public byte[] Version { get; set; } = [];
}

public sealed class PurchaseRequest : TenantRow
{
    public Guid DepartmentId { get; set; }
    public Department Department { get; set; } = null!;
    [MaxLength(100)] public string Name { get; set; } = "";
    [MaxLength(253)] public string Domain { get; set; } = "";
    [MaxLength(80)] public string Category { get; set; } = "";
    [MaxLength(600)] public string Reason { get; set; } = "";
    public decimal MonthlyCost { get; set; }
    public Currency Currency { get; set; }
    public PurchaseState State { get; set; }
    public DateTime CreatedAt { get; set; }
    [MaxLength(120)] public string CreatedBy { get; set; } = "";
    [Timestamp] public byte[] Version { get; set; } = [];
    public List<PurchaseDecision> Decisions { get; set; } = [];
}

public sealed class PurchaseDecision : TenantRow
{
    public Guid PurchaseRequestId { get; set; }
    public PurchaseRequest PurchaseRequest { get; set; } = null!;
    public PurchaseState FromState { get; set; }
    public PurchaseState ToState { get; set; }
    [MaxLength(120)] public string Actor { get; set; } = "";
    [MaxLength(400)] public string Comment { get; set; } = "";
    public DateTime DecidedAt { get; set; }
}

public sealed class FxRate : TenantRow
{
    public Currency Currency { get; set; }
    public DateOnly EffectiveDate { get; set; }
    public decimal UsdPerUnit { get; set; }
}

public sealed class RadarAlert : TenantRow
{
    public Guid ContractId { get; set; }
    public Contract Contract { get; set; } = null!;
    public RadarKind Kind { get; set; }
    public DateOnly DueOn { get; set; }
    public DateTime DetectedAt { get; set; }
}
