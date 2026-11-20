using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VendorRadar.Application;
using VendorRadar.Domain;
using VendorRadar.Infrastructure;

namespace VendorRadar.Web;

public sealed class OverviewController(RadarDb db, IAnalytics analytics, ICurrentActor actor, TimeProvider clock,
    IConfiguration settings, PdfReports reports) : BaseController
{
    private DateOnly Today => DateOnly.FromDateTime(clock.GetLocalNow().DateTime);

    [HttpGet("/")]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var workspace = await db.Organizations.AsNoTracking().SingleOrDefaultAsync(ct);
        if (workspace is null) return NotFound();
        var snapshot = await analytics.ReadAsync(ct);
        var contracts = await db.Contracts.AsNoTracking().Where(x => x.Active).ToListAsync(ct);
        var horizon = Math.Clamp(settings.GetValue("Radar:HorizonDays", 30), 1, 90);
        var radar = ContractRadar.UpcomingFor(contracts, Today, horizon).OrderBy(x => x.Date).ToArray();
        var runs = await db.AccessRuns.AsNoTracking().Include(x => x.Employee).Include(x => x.Tasks)
            .Where(x => x.CompletedAt == null && (actor.Value.Role != Roles.Lead || x.Employee.DepartmentId == actor.Value.DepartmentId))
            .OrderByDescending(x => x.StartedAt).ToListAsync(ct);
        var purchases = await db.PurchaseRequests.CountAsync(x =>
            (x.State == PurchaseState.DepartmentReview || x.State == PurchaseState.FinanceReview) &&
            (actor.Value.Role != Roles.Lead || x.DepartmentId == actor.Value.DepartmentId), ct);
        var model = new Overview(workspace, snapshot, radar.Where(x => x.Kind == RadarKind.Notice).ToArray(),
            radar.Where(x => x.Kind == RadarKind.Payment).ToArray(), runs, purchases, Today);
        return IsHtmx ? PartialView("_Overview", model) : View(model);
    }

    [HttpGet("/calendar")]
    public async Task<IActionResult> Calendar(int? days, CancellationToken ct)
    {
        var horizon = days is 30 or 60 or 90 ? days.Value : 90;
        ViewBag.Days = horizon;
        ViewBag.Today = Today;
        var contracts = await db.Contracts.AsNoTracking().Where(x => x.Active).ToListAsync(ct);
        return View(ContractRadar.UpcomingFor(contracts, Today, horizon).OrderBy(x => x.Date).ToList());
    }

    [HttpGet("/reports/finance.pdf"), Authorize(Roles = Roles.Finance)]
    public async Task<IActionResult> FinanceReport(CancellationToken ct)
    {
        var workspace = await db.Organizations.AsNoTracking().SingleAsync(ct);
        return File(reports.Finance(workspace.Name, await analytics.ReadAsync(ct), Today), "application/pdf", $"vendor-radar-finance-{Today:yyyy-MM-dd}.pdf");
    }

    [HttpGet("/error"), AllowAnonymous]
    public IActionResult Error()
    {
        Response.StatusCode = 500;
        return View("~/Views/Shared/Error.cshtml");
    }
}
