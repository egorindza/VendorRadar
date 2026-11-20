using System.Data;
using Microsoft.EntityFrameworkCore;
using VendorRadar.Application;
using VendorRadar.Domain;

namespace VendorRadar.Infrastructure;

public sealed class ContractRegistry(RadarDb db, ICurrentActor current, IWorkspaceEvents events)
{
    public async Task<Guid> SaveAsync(Guid? id, Contract input, byte[]? version, CancellationToken ct)
    {
        var actor = current.Value;
        if (actor.Role != Roles.Finance) throw new OperationDeniedException();
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        if (!await db.Departments.AnyAsync(x => x.Id == input.DepartmentId, ct))
            throw new BusinessException("Выберите отдел.");
        var entity = id is not { } contractId
            ? new Contract { TenantId = actor.TenantId }
            : await db.Contracts.SingleOrDefaultAsync(x => x.Id == contractId, ct) ?? throw new MissingRecordException();
        if (id is null) db.Add(entity);
        else if (version is null || !version.AsSpan().SequenceEqual(entity.Version))
            throw new BusinessException("Контракт уже изменён. Проверьте текущую версию.");
        var currentSeats = await db.AccessGrants.CountAsync(x =>
            x.ContractId == entity.Id && x.Kind == AccessKind.Seat && x.RevokedAt == null, ct);
        if (input.Seats < currentSeats)
            throw new BusinessException($"Сначала освободите назначенные места ({currentSeats}).");
        if (!input.Active && await db.AccessGrants.AnyAsync(x => x.ContractId == entity.Id && x.RevokedAt == null, ct))
            throw new BusinessException("Сначала подтвердите отзыв действующих доступов к сервису.");

        entity.Name = input.Name.Trim();
        entity.Domain = BusinessRules.NormalizeDomain(input.Domain);
        entity.Category = input.Category.Trim();
        entity.DepartmentId = input.DepartmentId;
        entity.Currency = input.Currency;
        entity.Cycle = input.Cycle;
        entity.Cost = input.Cost;
        entity.Seats = input.Seats;
        entity.NextPayment = input.NextPayment;
        entity.NoticeDays = input.NoticeDays;
        entity.AutoRenew = input.AutoRenew;
        entity.Active = input.Active;
        entity.ChangedBy = actor.Name;
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        await events.PublishAsync(actor.TenantId, ct);
        return entity.Id;
    }

    public async Task AddRateAsync(Currency currency, DateOnly on, decimal usdPerUnit, CancellationToken ct)
    {
        var actor = current.Value;
        if (actor.Role != Roles.Finance) throw new OperationDeniedException();
        if (currency == Currency.USD || usdPerUnit <= 0)
            throw new BusinessException("Укажите положительный курс EUR или GBP.");
        db.FxRates.Add(new FxRate { TenantId = actor.TenantId, Currency = currency, EffectiveDate = on, UsdPerUnit = usdPerUnit });
        await db.SaveChangesAsync(ct);
        await events.PublishAsync(actor.TenantId, ct);
    }
}
