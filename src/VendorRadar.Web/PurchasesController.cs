using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VendorRadar.Application;
using VendorRadar.Domain;
using VendorRadar.Infrastructure;

namespace VendorRadar.Web;

[Route("purchases")]
public sealed class PurchasesController(RadarDb db, Purchasing purchasing) : BaseController
{
    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken ct) =>
        View(await purchasing.Visible().AsNoTracking().Include(x => x.Department).OrderByDescending(x => x.CreatedAt).ToListAsync(ct));

    [HttpGet("new")]
    public async Task<IActionResult> New(CancellationToken ct)
    {
        ViewBag.Departments = await db.Departments.AsNoTracking().OrderBy(x => x.Name).ToListAsync(ct);
        return View(new PurchaseForm());
    }

    [HttpPost("new")]
    public Task<IActionResult> Submit(PurchaseForm form, CancellationToken ct) => Change(async () =>
    {
        var id = await purchasing.SubmitAsync(new PurchaseRequest
        {
            Name = form.Name, Domain = form.Domain, DepartmentId = form.DepartmentId, Category = form.Category,
            Reason = form.Reason, MonthlyCost = form.MonthlyCost, Currency = form.Currency
        }, ct);
        TempData["Success"] = "Заявка отправлена руководителю отдела.";
        return Redirect($"/purchases/{id}");
    }, () => Redirect("/purchases/new"));

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Details(Guid id, CancellationToken ct)
    {
        var item = await purchasing.Visible().AsNoTracking().Include(x => x.Department).Include(x => x.Decisions)
            .SingleOrDefaultAsync(x => x.Id == id, ct);
        if (item is null) return NotFound();
        return View(new PurchaseDetails(item, await purchasing.DuplicatesAsync(item, ct), purchasing.CanDecide(item)));
    }

    [HttpPost("{id:guid}/decision")]
    public Task<IActionResult> Decision(Guid id, bool approve, [StringLength(400)] string? comment, CancellationToken ct) =>
        Change(async () =>
        {
            await purchasing.DecideAsync(id, approve, comment, ct);
            TempData["Success"] = "Решение сохранено.";
            return Redirect($"/purchases/{id}");
        }, () => Redirect($"/purchases/{id}"));
}
