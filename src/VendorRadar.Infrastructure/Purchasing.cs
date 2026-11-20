using Microsoft.EntityFrameworkCore;
using VendorRadar.Application;
using VendorRadar.Domain;

namespace VendorRadar.Infrastructure;

public sealed class Purchasing(RadarDb db, ICurrentActor current, TimeProvider time, IWorkspaceEvents events)
{
    private Actor Actor => current.Value;

    public IQueryable<PurchaseRequest> Visible() => db.PurchaseRequests
        .Where(x => Actor.Role != Roles.Lead || x.DepartmentId == Actor.DepartmentId);

    public async Task<Guid> SubmitAsync(PurchaseRequest request, CancellationToken ct)
    {
        if (Actor.Role == Roles.Lead)
            request.DepartmentId = Actor.DepartmentId ?? throw new OperationDeniedException();
        if (!await db.Departments.AnyAsync(x => x.Id == request.DepartmentId, ct))
            throw new BusinessException("Выберите отдел.");
        request.TenantId = Actor.TenantId;
        request.Domain = BusinessRules.NormalizeDomain(request.Domain);
        request.Name = request.Name.Trim();
        request.Category = request.Category.Trim();
        request.Reason = request.Reason.Trim();
        request.State = PurchaseState.DepartmentReview;
        request.CreatedBy = Actor.Name;
        request.CreatedAt = time.GetUtcNow().UtcDateTime;
        db.PurchaseRequests.Add(request);
        await db.SaveChangesAsync(ct);
        await events.PublishAsync(Actor.TenantId, ct);
        return request.Id;
    }

    public async Task<IReadOnlyList<Contract>> DuplicatesAsync(PurchaseRequest request, CancellationToken ct) =>
        await db.Contracts.AsNoTracking().Where(x => x.Active &&
            (x.Domain == request.Domain || x.Name == request.Name || x.Category == request.Category))
            .OrderByDescending(x => x.Domain == request.Domain).ThenBy(x => x.Name).ToListAsync(ct);

    public bool CanDecide(PurchaseRequest item) => item.State switch
    {
        PurchaseState.DepartmentReview => Actor.Role == Roles.Lead && item.DepartmentId == Actor.DepartmentId,
        PurchaseState.FinanceReview => Actor.Role == Roles.Finance,
        _ => false
    };

    public async Task DecideAsync(Guid id, bool approve, string? comment, CancellationToken ct)
    {
        var request = await Visible().SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new MissingRecordException();
        if (!CanDecide(request)) throw new OperationDeniedException();
        if ((!approve || (await DuplicatesAsync(request, ct)).Count > 0) && string.IsNullOrWhiteSpace(comment))
            throw new BusinessException("Укажите причину решения, если есть совпадения или закупка отклонена.");

        var next = BusinessRules.Decide(request.State, approve);
        db.PurchaseDecisions.Add(new PurchaseDecision
        {
            TenantId = Actor.TenantId, PurchaseRequestId = id, FromState = request.State, ToState = next,
            Actor = Actor.Name, Comment = comment?.Trim() ?? "", DecidedAt = time.GetUtcNow().UtcDateTime
        });
        request.State = next;
        await db.SaveChangesAsync(ct);
        await events.PublishAsync(Actor.TenantId, ct);
    }
}
