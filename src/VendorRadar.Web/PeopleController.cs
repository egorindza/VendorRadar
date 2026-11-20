using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VendorRadar.Application;
using VendorRadar.Domain;
using VendorRadar.Infrastructure;

namespace VendorRadar.Web;

[Route("people")]
public sealed class PeopleController(RadarDb db, IAnalytics analytics, AccessWorkflows workflows,
    ICurrentActor actor, PdfReports reports) : BaseController
{
    private IQueryable<Employee> VisiblePeople() => db.Employees.AsNoTracking()
        .Where(x => actor.Value.Role != Roles.Lead || x.DepartmentId == actor.Value.DepartmentId);

    private IQueryable<AccessRun> VisibleRuns() => db.AccessRuns.AsNoTracking()
        .Where(x => actor.Value.Role != Roles.Lead || x.Employee.DepartmentId == actor.Value.DepartmentId);

    [HttpGet("")]
    public async Task<IActionResult> Index(string? q, CancellationToken ct)
    {
        ViewBag.Query = q;
        var people = VisiblePeople().Include(x => x.Department).AsQueryable();
        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim();
            people = people.Where(x => x.Name.Contains(term) || x.Email.Contains(term) || x.Job.Contains(term));
        }
        var employees = await people.OrderBy(x => x.Name).ToListAsync(ct);
        var ids = employees.Select(x => x.Id).ToArray();
        var services = await db.Contracts.AsNoTracking().Where(x => x.Active).OrderBy(x => x.Name).ToListAsync(ct);
        var grants = await db.AccessGrants.AsNoTracking().Include(x => x.Contract)
            .Where(x => ids.Contains(x.EmployeeId) && x.RevokedAt == null).ToListAsync(ct);
        return View(new Matrix(employees, services, grants, (await analytics.ReadAsync(ct)).Seats));
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Details(Guid id, CancellationToken ct)
    {
        var employee = await VisiblePeople().Include(x => x.Department).SingleOrDefaultAsync(x => x.Id == id, ct);
        if (employee is null) return NotFound();
        var grants = await db.AccessGrants.AsNoTracking().Include(x => x.Contract).Where(x => x.EmployeeId == id)
            .OrderBy(x => x.RevokedAt != null).ThenBy(x => x.Contract.Name).ToListAsync(ct);
        var runs = await VisibleRuns().Include(x => x.Tasks).Where(x => x.EmployeeId == id).OrderByDescending(x => x.StartedAt).ToListAsync(ct);
        var services = await db.Contracts.AsNoTracking().Where(x => x.Active).OrderBy(x => x.Name).ToListAsync(ct);
        return View(new EmployeeDetails(employee, grants, runs, await analytics.GrantHistoryAsync(id, ct), services));
    }

    [HttpGet("hire"), Authorize(Roles = Roles.Security)]
    public async Task<IActionResult> Hire(CancellationToken ct)
    {
        ViewBag.Departments = await db.Departments.AsNoTracking().OrderBy(x => x.Name).ToListAsync(ct);
        ViewBag.Templates = await db.JobTemplates.AsNoTracking().Include(x => x.Items).ThenInclude(x => x.Contract)
            .OrderBy(x => x.Name).ToListAsync(ct);
        return View(new HireForm());
    }

    [HttpPost("hire"), Authorize(Roles = Roles.Security)]
    public Task<IActionResult> StartHire(HireForm form, CancellationToken ct) => Change(async () =>
        Redirect($"/people/runs/{await workflows.OnboardAsync(form.Name, form.Email, form.DepartmentId, form.TemplateId, ct)}"),
        () => Redirect("/people/hire"));

    [HttpGet("templates"), Authorize(Roles = Roles.Security)]
    public async Task<IActionResult> Templates(CancellationToken ct)
    {
        ViewBag.Templates = await db.JobTemplates.AsNoTracking().Include(x => x.Items).ThenInclude(x => x.Contract)
            .OrderBy(x => x.Name).ToListAsync(ct);
        return View(await db.Contracts.AsNoTracking().Where(x => x.Active).OrderBy(x => x.Name).ToListAsync(ct));
    }

    [HttpPost("templates"), Authorize(Roles = Roles.Security)]
    public Task<IActionResult> CreateTemplate([Required, StringLength(100)] string name, Guid[] selected,
        AccessRole role, CancellationToken ct) => Change(async () =>
    {
        if (!Enum.IsDefined(role)) throw new BusinessException("Выберите роль доступа.");
        await workflows.AddTemplateAsync(name, selected, role, ct);
        TempData["Success"] = "Шаблон сохранён.";
        return Redirect("/people/templates");
    }, () => Redirect("/people/templates"));

    [HttpPost("{id:guid}/offboard"), Authorize(Roles = Roles.Security)]
    public Task<IActionResult> Offboard(Guid id, CancellationToken ct) => Change(async () =>
        Redirect($"/people/runs/{await workflows.OffboardAsync(id, ct)}"),
        () => Redirect($"/people/{id}"));

    [HttpPost("grants"), Authorize(Roles = Roles.Security)]
    public Task<IActionResult> Grant(GrantForm form, CancellationToken ct) => Change(async () =>
    {
        await workflows.RecordGrantAsync(form.EmployeeId, form.ContractId, form.Role, form.Kind, form.Label, ct);
        TempData["Success"] = "Доступ записан в реестр.";
        return Redirect($"/people/{form.EmployeeId}");
    }, () => Redirect($"/people/{form.EmployeeId}"));

    [HttpPost("{id:guid}/grants/{grantId:guid}"), Authorize(Roles = Roles.Security)]
    public Task<IActionResult> UpdateGrant(Guid id, Guid grantId, string action, CancellationToken ct) => Change(async () =>
    {
        await workflows.UpdateGrantAsync(id, grantId, action, ct);
        TempData["Success"] = "Доступ обновлён.";
        return Redirect($"/people/{id}");
    }, () => Redirect($"/people/{id}"));

    [HttpGet("runs")]
    public async Task<IActionResult> Runs(CancellationToken ct) =>
        View(await VisibleRuns().Include(x => x.Employee).Include(x => x.Tasks).OrderByDescending(x => x.StartedAt).ToListAsync(ct));

    [HttpGet("runs/{id:guid}")]
    public async Task<IActionResult> Run(Guid id, CancellationToken ct)
    {
        var run = await RunAsync(id, ct);
        if (run is null) return NotFound();
        return IsHtmx ? PartialView("_Run", run) : View(run);
    }

    [HttpPost("runs/{id:guid}/tasks/{taskId:guid}"), Authorize(Roles = Roles.Security)]
    public Task<IActionResult> Confirm(Guid id, Guid taskId, [Required, StringLength(400)] string evidence, CancellationToken ct) =>
        Change(async () =>
        {
            await workflows.ConfirmAsync(id, taskId, evidence, ct);
            return IsHtmx ? PartialView("_Run", await RunAsync(id, ct)) : Redirect($"/people/runs/{id}");
        }, () => Redirect($"/people/runs/{id}"));

    [HttpGet("runs/{id:guid}/closure.pdf"), Authorize(Roles = Roles.Security + "," + Roles.Finance)]
    public async Task<IActionResult> Closure(Guid id, CancellationToken ct)
    {
        var run = await RunAsync(id, ct);
        if (run is null) return NotFound();
        if (run.Kind != RunKind.Offboarding || run.CompletedAt is null) return Conflict();
        var tenant = await db.Organizations.AsNoTracking().SingleAsync(ct);
        return File(await reports.ClosureAsync(tenant.Name, run), "application/pdf", $"vendor-radar-closure-{id:N}.pdf");
    }

    private Task<AccessRun?> RunAsync(Guid id, CancellationToken ct) =>
        VisibleRuns().Include(x => x.Employee).Include(x => x.Tasks).SingleOrDefaultAsync(x => x.Id == id, ct);
}
