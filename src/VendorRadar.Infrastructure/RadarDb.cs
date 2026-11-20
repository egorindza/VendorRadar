using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VendorRadar.Application;
using VendorRadar.Domain;

namespace VendorRadar.Infrastructure;

public sealed class RadarDb(DbContextOptions<RadarDb> options, ICurrentActor actor) : DbContext(options)
{
    private Guid CurrentTenant => actor.Value.TenantId;

    public DbSet<Organization> Organizations => Set<Organization>();
    public DbSet<Department> Departments => Set<Department>();
    public DbSet<Contract> Contracts => Set<Contract>();
    public DbSet<Employee> Employees => Set<Employee>();
    public DbSet<AccessGrant> AccessGrants => Set<AccessGrant>();
    public DbSet<JobTemplate> JobTemplates => Set<JobTemplate>();
    public DbSet<TemplateItem> TemplateItems => Set<TemplateItem>();
    public DbSet<AccessRun> AccessRuns => Set<AccessRun>();
    public DbSet<RunTask> RunTasks => Set<RunTask>();
    public DbSet<PurchaseRequest> PurchaseRequests => Set<PurchaseRequest>();
    public DbSet<PurchaseDecision> PurchaseDecisions => Set<PurchaseDecision>();
    public DbSet<FxRate> FxRates => Set<FxRate>();
    public DbSet<RadarAlert> RadarAlerts => Set<RadarAlert>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        var organizations = Scoped<Organization>(model);
        organizations.ToTable("Organizations", t => t.HasCheckConstraint("CK_Organizations_Tenant", "[Id] = [TenantId]"));
        Scoped<Department>(model).HasIndex(x => new { x.TenantId, x.Name }).IsUnique();

        var contracts = Scoped<Contract>(model);
        contracts.ToTable("Contracts", t =>
        {
            t.IsTemporal(temporal => temporal.UseHistoryTable("ContractHistory"));
            t.HasCheckConstraint("CK_Contracts_Values", "[Cost] >= 0 AND [Seats] >= 0 AND [NoticeDays] BETWEEN 0 AND 365");
        });
        contracts.Property(x => x.Cost).HasPrecision(19, 4);
        contracts.Property(x => x.Currency).HasConversion<string>().HasMaxLength(3);
        contracts.Property(x => x.Cycle).HasConversion<string>().HasMaxLength(12);
        contracts.HasIndex(x => new { x.TenantId, x.Active, x.NextPayment });
        contracts.HasIndex(x => new { x.TenantId, x.Domain }).IsUnique().HasFilter("[Active] = 1");
        contracts.HasOne(x => x.Department).WithMany().HasForeignKey(x => new { x.TenantId, x.DepartmentId });

        var employees = Scoped<Employee>(model);
        employees.HasIndex(x => new { x.TenantId, x.Email }).IsUnique();
        employees.HasOne(x => x.Department).WithMany().HasForeignKey(x => new { x.TenantId, x.DepartmentId });

        var grants = Scoped<AccessGrant>(model);
        grants.ToTable("AccessGrants", t => t.IsTemporal(temporal => temporal.UseHistoryTable("AccessGrantHistory")));
        grants.Property(x => x.Kind).HasConversion<string>().HasMaxLength(12);
        grants.Property(x => x.Role).HasConversion<string>().HasMaxLength(12);
        grants.HasIndex(x => new { x.TenantId, x.EmployeeId, x.ContractId, x.Kind, x.AssetLabel })
            .IsUnique().HasFilter("[RevokedAt] IS NULL");
        grants.HasOne(x => x.Contract).WithMany().HasForeignKey(x => new { x.TenantId, x.ContractId });
        grants.HasOne(x => x.Employee).WithMany().HasForeignKey(x => new { x.TenantId, x.EmployeeId });

        Scoped<JobTemplate>(model).HasIndex(x => new { x.TenantId, x.Name }).IsUnique();
        var items = Scoped<TemplateItem>(model);
        items.HasOne(x => x.JobTemplate).WithMany(x => x.Items).HasForeignKey(x => new { x.TenantId, x.JobTemplateId });
        items.HasOne(x => x.Contract).WithMany().HasForeignKey(x => new { x.TenantId, x.ContractId });
        items.HasIndex(x => new { x.TenantId, x.JobTemplateId, x.ContractId }).IsUnique();

        var runs = Scoped<AccessRun>(model);
        runs.HasOne(x => x.Employee).WithMany().HasForeignKey(x => new { x.TenantId, x.EmployeeId });
        runs.HasIndex(x => new { x.TenantId, x.EmployeeId }).IsUnique().HasFilter("[CompletedAt] IS NULL");
        runs.HasIndex(x => new { x.TenantId, x.EmployeeId, x.Kind }).IsUnique().HasFilter("[Kind] = 1");
        var tasks = Scoped<RunTask>(model);
        tasks.HasOne(x => x.AccessRun).WithMany(x => x.Tasks).HasForeignKey(x => new { x.TenantId, x.AccessRunId });
        tasks.HasOne(x => x.Contract).WithMany().HasForeignKey(x => new { x.TenantId, x.ContractId });
        tasks.HasOne(x => x.AccessGrant).WithMany().HasForeignKey(x => new { x.TenantId, x.AccessGrantId });

        var purchases = Scoped<PurchaseRequest>(model);
        purchases.Property(x => x.MonthlyCost).HasPrecision(19, 4);
        purchases.HasOne(x => x.Department).WithMany().HasForeignKey(x => new { x.TenantId, x.DepartmentId });
        Scoped<PurchaseDecision>(model).HasOne(x => x.PurchaseRequest).WithMany(x => x.Decisions)
            .HasForeignKey(x => new { x.TenantId, x.PurchaseRequestId });

        var rates = Scoped<FxRate>(model);
        rates.ToTable("FxRates", t => t.HasCheckConstraint("CK_FxRates_Positive", "[UsdPerUnit] > 0"));
        rates.Property(x => x.Currency).HasConversion<string>().HasMaxLength(3);
        rates.Property(x => x.UsdPerUnit).HasPrecision(19, 8);
        rates.HasIndex(x => new { x.TenantId, x.Currency, x.EffectiveDate }).IsUnique();

        var alerts = Scoped<RadarAlert>(model);
        alerts.HasOne(x => x.Contract).WithMany().HasForeignKey(x => new { x.TenantId, x.ContractId });
        alerts.HasIndex(x => new { x.TenantId, x.ContractId, x.Kind, x.DueOn }).IsUnique();
        foreach (var foreignKey in model.Model.GetEntityTypes().SelectMany(x => x.GetForeignKeys()))
            foreignKey.DeleteBehavior = DeleteBehavior.Restrict;
    }

    private EntityTypeBuilder<T> Scoped<T>(ModelBuilder model) where T : TenantRow
    {
        var entity = model.Entity<T>();
        entity.HasKey(x => new { x.TenantId, x.Id });
        entity.HasQueryFilter(x => x.TenantId == CurrentTenant);
        if (typeof(T) != typeof(Organization))
            entity.HasOne<Organization>().WithMany().HasForeignKey(nameof(TenantRow.TenantId)).HasPrincipalKey(x => x.TenantId);
        return entity;
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        CheckTenancy();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        CheckTenancy();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void CheckTenancy()
    {
        foreach (var row in ChangeTracker.Entries<TenantRow>().Where(x =>
            x.State is EntityState.Added or EntityState.Modified or EntityState.Deleted))
        {
            if (row.Entity.TenantId != CurrentTenant || CurrentTenant == Guid.Empty)
                throw new OperationDeniedException();
        }
    }
}
