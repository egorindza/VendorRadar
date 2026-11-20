using System.Data;
using Microsoft.EntityFrameworkCore;
using VendorRadar.Application;
using VendorRadar.Domain;

namespace VendorRadar.Infrastructure;

public sealed class AccessWorkflows(RadarDb db, ICurrentActor current, TimeProvider time, IWorkspaceEvents events)
{
    private Actor Actor => current.Value;
    private DateTime Now => time.GetUtcNow().UtcDateTime;

    public async Task<Guid> OnboardAsync(string name, string email, Guid departmentId, Guid templateId, CancellationToken ct)
    {
        RequireSecurity();
        var template = await db.JobTemplates.Include(x => x.Items).ThenInclude(x => x.Contract)
            .SingleOrDefaultAsync(x => x.Id == templateId, ct) ?? throw new BusinessException("Выберите шаблон должности.");
        if (!await db.Departments.AnyAsync(x => x.Id == departmentId, ct))
            throw new BusinessException("Выберите отдел.");
        if (template.Items.Count == 0 || template.Items.Any(x => !x.Contract.Active))
            throw new BusinessException("В шаблоне должны быть действующие сервисы.");
        var employee = new Employee
        {
            TenantId = Actor.TenantId, DepartmentId = departmentId, Name = name.Trim(),
            Email = email.Trim().ToLowerInvariant(), Job = template.Name, Status = EmployeeStatus.Onboarding
        };
        var run = NewRun(employee, RunKind.Onboarding);
        foreach (var item in template.Items)
            run.Tasks.Add(NewTask(run, item.Contract, item.Role, AccessKind.Seat, ""));
        db.AccessRuns.Add(run);
        await db.SaveChangesAsync(ct);
        await events.PublishAsync(Actor.TenantId, ct);
        return run.Id;
    }

    public async Task<Guid> OffboardAsync(Guid employeeId, CancellationToken ct)
    {
        RequireSecurity();
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var employee = await EmployeeAsync(employeeId, ct);
        var existing = await db.AccessRuns.SingleOrDefaultAsync(x => x.EmployeeId == employeeId && x.Kind == RunKind.Offboarding, ct);
        if (existing is not null) return existing.Id;
        if (employee.Status == EmployeeStatus.Departed) throw new BusinessException("Сотрудник уже уволен.");

        var onboarding = await db.AccessRuns.SingleOrDefaultAsync(x => x.EmployeeId == employeeId && x.CompletedAt == null, ct);
        if (onboarding is not null)
        {
            onboarding.CompletedAt = Now;
            onboarding.CompletedBy = Actor.Name;
            await db.SaveChangesAsync(ct);
        }

        employee.Status = EmployeeStatus.Offboarding;
        var run = NewRun(employee, RunKind.Offboarding);
        var grants = await db.AccessGrants.Include(x => x.Contract)
            .Where(x => x.EmployeeId == employeeId && x.RevokedAt == null).ToListAsync(ct);
        foreach (var grant in grants.OrderByDescending(x => x.Role).ThenByDescending(x => x.Kind))
        {
            var task = NewTask(run, grant.Contract, grant.Role, grant.Kind, grant.AssetLabel);
            task.AccessGrantId = grant.Id;
            run.Tasks.Add(task);
        }
        if (run.Tasks.Count == 0) CloseRun(run, Now);
        db.AccessRuns.Add(run);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        await events.PublishAsync(Actor.TenantId, ct);
        return run.Id;
    }

    public async Task ConfirmAsync(Guid runId, Guid taskId, string evidence, CancellationToken ct)
    {
        RequireSecurity();
        if (string.IsNullOrWhiteSpace(evidence)) throw new BusinessException("Укажите подтверждение или номер задачи.");
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var run = await db.AccessRuns.Include(x => x.Employee).Include(x => x.Tasks)
            .SingleOrDefaultAsync(x => x.Id == runId, ct) ?? throw new MissingRecordException();
        var task = run.Tasks.SingleOrDefault(x => x.Id == taskId) ?? throw new MissingRecordException();
        if (task.CompletedAt is not null) return;
        if (run.CompletedAt is not null) throw new BusinessException("Чек-лист уже закрыт.");
        var now = Now;
        if (run.Kind == RunKind.Offboarding)
        {
            var grant = await db.AccessGrants.SingleAsync(x => x.Id == task.AccessGrantId, ct);
            Revoke(grant, now);
        }
        else
        {
            var grant = await AssignAsync(run.Employee, task.ContractId, task.Role, task.Kind, task.AssetLabel, now, ct);
            task.AccessGrantId = grant.Id;
        }
        task.CompletedAt = now;
        task.CompletedBy = Actor.Name;
        task.Evidence = evidence.Trim();
        if (run.Tasks.All(x => x.CompletedAt is not null)) CloseRun(run, now);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        await events.PublishAsync(Actor.TenantId, ct);
    }

    public async Task RecordGrantAsync(Guid employeeId, Guid contractId, AccessRole role, AccessKind kind, string? label, CancellationToken ct)
    {
        RequireSecurity();
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var employee = await EmployeeAsync(employeeId, ct);
        if (employee.Status != EmployeeStatus.Active)
            throw new BusinessException("Выдача вне чек-листа доступна только активному сотруднику.");
        var asset = kind == AccessKind.Seat ? "" : label?.Trim();
        if (kind == AccessKind.ApiKey && string.IsNullOrWhiteSpace(asset))
            throw new BusinessException("Укажите идентификатор ключа в системе поставщика.");
        await AssignAsync(employee, contractId, role, kind, asset ?? "", Now, ct);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        await events.PublishAsync(Actor.TenantId, ct);
    }

    public async Task UpdateGrantAsync(Guid employeeId, Guid grantId, string action, CancellationToken ct)
    {
        RequireSecurity();
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var employee = await EmployeeAsync(employeeId, ct);
        var grant = await db.AccessGrants.SingleOrDefaultAsync(x => x.Id == grantId && x.EmployeeId == employeeId, ct)
            ?? throw new MissingRecordException();
        if (employee.Status == EmployeeStatus.Offboarding)
            throw new BusinessException("Подтвердите отзыв в чек-листе увольнения.");
        if (grant.RevokedAt is not null) return;
        switch (action)
        {
            case "revoke": Revoke(grant, Now); break;
            case "used": grant.LastUsedAt = Now; grant.ChangedBy = Actor.Name; break;
            default: throw new BusinessException("Неизвестное действие.");
        }
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        await events.PublishAsync(Actor.TenantId, ct);
    }

    public async Task AddTemplateAsync(string name, IReadOnlyList<Guid> selected, AccessRole role, CancellationToken ct)
    {
        RequireSecurity();
        var ids = selected.Distinct().ToArray();
        var contracts = await db.Contracts.Where(x => x.Active && ids.Contains(x.Id)).ToListAsync(ct);
        if (contracts.Count == 0 || contracts.Count != ids.Length)
            throw new BusinessException("Выберите действующие сервисы.");
        var template = new JobTemplate { TenantId = Actor.TenantId, Name = name.Trim() };
        foreach (var contract in contracts)
            template.Items.Add(new TemplateItem { TenantId = Actor.TenantId, ContractId = contract.Id, Role = role });
        db.JobTemplates.Add(template);
        await db.SaveChangesAsync(ct);
    }

    private async Task<AccessGrant> AssignAsync(Employee employee, Guid contractId, AccessRole role, AccessKind kind, string label, DateTime now, CancellationToken ct)
    {
        var contract = await db.Contracts.SingleOrDefaultAsync(x => x.Id == contractId && x.Active, ct)
            ?? throw new BusinessException("Выберите действующий сервис.");
        var existing = await db.AccessGrants.SingleOrDefaultAsync(x =>
            x.EmployeeId == employee.Id && x.ContractId == contractId && x.Kind == kind && x.AssetLabel == label && x.RevokedAt == null, ct);
        if (existing is not null)
        {
            existing.Role = role;
            existing.ChangedBy = Actor.Name;
            return existing;
        }
        if (kind == AccessKind.Seat && await db.AccessGrants.CountAsync(x =>
            x.ContractId == contractId && x.Kind == AccessKind.Seat && x.RevokedAt == null, ct) >= contract.Seats)
            throw new BusinessException("У сервиса нет свободных оплаченных мест.");
        var grant = new AccessGrant
        {
            TenantId = Actor.TenantId, EmployeeId = employee.Id, ContractId = contract.Id,
            Role = role, Kind = kind, AssetLabel = label, GrantedAt = now, ChangedBy = Actor.Name
        };
        db.AccessGrants.Add(grant);
        return grant;
    }

    private async Task<Employee> EmployeeAsync(Guid id, CancellationToken ct) =>
        await db.Employees.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new MissingRecordException();

    private AccessRun NewRun(Employee employee, RunKind kind) => new()
    {
        TenantId = Actor.TenantId, Employee = employee, Kind = kind, StartedAt = Now, StartedBy = Actor.Name
    };

    private RunTask NewTask(AccessRun run, Contract contract, AccessRole role, AccessKind kind, string label) => new()
    {
        TenantId = Actor.TenantId, AccessRunId = run.Id, ContractId = contract.Id,
        Role = role, Kind = kind, AssetLabel = label, ServiceName = contract.Name
    };

    private void Revoke(AccessGrant grant, DateTime now)
    {
        if (grant.RevokedAt is not null) return;
        grant.RevokedAt = now;
        grant.ChangedBy = Actor.Name;
    }

    private void CloseRun(AccessRun run, DateTime now)
    {
        run.CompletedAt = now;
        run.CompletedBy = Actor.Name;
        run.Employee.Status = run.Kind == RunKind.Offboarding ? EmployeeStatus.Departed : EmployeeStatus.Active;
    }

    private void RequireSecurity()
    {
        if (Actor.Role != Roles.Security) throw new OperationDeniedException();
    }
}
