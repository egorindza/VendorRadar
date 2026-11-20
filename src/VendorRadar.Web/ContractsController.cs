using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VendorRadar.Application;
using VendorRadar.Domain;
using VendorRadar.Infrastructure;

namespace VendorRadar.Web;

[Route("contracts")]
public sealed class ContractsController(RadarDb db, IAnalytics analytics, ContractRegistry registry, TimeProvider clock) : BaseController
{
    [HttpGet("")]
    public async Task<IActionResult> Index(string? q, bool archived, CancellationToken ct)
    {
        ViewBag.Query = q;
        ViewBag.Archived = archived;
        var rows = db.Contracts.AsNoTracking().Include(x => x.Department).Where(x => x.Active != archived);
        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim();
            rows = rows.Where(x => x.Name.Contains(term) || x.Domain.Contains(term) || x.Category.Contains(term));
        }
        var items = await rows.OrderBy(x => x.Name).ToListAsync(ct);
        return IsHtmx ? PartialView("_Rows", items) : View(items);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Details(Guid id, CancellationToken ct)
    {
        var item = await db.Contracts.AsNoTracking().Include(x => x.Department).SingleOrDefaultAsync(x => x.Id == id, ct);
        if (item is null) return NotFound();
        var seats = (await analytics.ReadAsync(ct)).Seats.SingleOrDefault(x => x.ContractId == id);
        return View(new ContractDetails(item, seats, await analytics.CostHistoryAsync(id, ct)));
    }

    [HttpGet("edit/{id:guid?}"), Authorize(Roles = Roles.Finance)]
    public async Task<IActionResult> Edit(Guid? id, CancellationToken ct)
    {
        ViewBag.Departments = await db.Departments.AsNoTracking().OrderBy(x => x.Name).ToListAsync(ct);
        if (id is null) return View(new ContractForm { NextPayment = DateOnly.FromDateTime(clock.GetLocalNow().DateTime).AddDays(30) });
        var item = await db.Contracts.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
        return item is null ? NotFound() : View(ContractForm.From(item));
    }

    [HttpPost("edit"), Authorize(Roles = Roles.Finance)]
    public Task<IActionResult> Save(ContractForm form, CancellationToken ct) => Change(async () =>
    {
        if (form.NextPayment.Year is < 2000 or > 2100)
            throw new BusinessException("Укажите дату платежа между 2000 и 2100 годами.");
        byte[]? version = null;
        if (form.Version is not null)
        {
            try { version = Convert.FromBase64String(form.Version); }
            catch (FormatException) { throw new BusinessException("Версия контракта устарела."); }
        }
        var id = await registry.SaveAsync(form.Id, form.ToContract(), version, ct);
        TempData["Success"] = "Контракт сохранён.";
        return Redirect($"/contracts/{id}");
    }, () => Redirect(form.Id is null ? "/contracts/edit" : $"/contracts/edit/{form.Id}"));

    [HttpGet("rates"), Authorize(Roles = Roles.Finance)]
    public async Task<IActionResult> Rates(CancellationToken ct) =>
        View(await db.FxRates.AsNoTracking().OrderByDescending(x => x.EffectiveDate).ThenBy(x => x.Currency).ToListAsync(ct));

    [HttpPost("rates"), Authorize(Roles = Roles.Finance)]
    public Task<IActionResult> AddRate(Currency currency, DateOnly on, decimal usdPerUnit, CancellationToken ct) => Change(async () =>
    {
        if (!Enum.IsDefined(currency) || on.Year is < 2000 or > 2100 || usdPerUnit is <= 0 or > 100000)
            throw new BusinessException("Проверьте валюту, дату и значение курса.");
        await registry.AddRateAsync(currency, on, usdPerUnit, ct);
        TempData["Success"] = "Курс добавлен.";
        return Redirect("/contracts/rates");
    }, () => Redirect("/contracts/rates"));
}
