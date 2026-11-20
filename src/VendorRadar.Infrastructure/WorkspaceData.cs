using Microsoft.EntityFrameworkCore;
using VendorRadar.Application;
using VendorRadar.Domain;

namespace VendorRadar.Infrastructure;

public sealed record FixedActor(Actor Value) : ICurrentActor;

public static class WorkspaceDb
{
    public static RadarDb Create(DatabaseSettings settings, Actor actor)
    {
        var scope = new FixedActor(actor);
        var options = new DbContextOptionsBuilder<RadarDb>().UseSqlServer(settings.ScopedConnectionString)
            .AddInterceptors(new TenantSessionInterceptor(settings, scope)).Options;
        return new RadarDb(options, scope);
    }

    public static async Task MigrateAsync(string adminConnection, CancellationToken ct)
    {
        var actor = new FixedActor(new(Guid.Empty, "Migration", ""));
        await using var db = new RadarDb(new DbContextOptionsBuilder<RadarDb>().UseSqlServer(adminConnection).Options, actor);
        await db.Database.MigrateAsync(ct);
    }
}

public static class DemoData
{
    public static readonly Guid Northstar = Guid.Parse("a1000000-0000-0000-0000-000000000001");
    public static readonly Guid Meridian = Guid.Parse("a2000000-0000-0000-0000-000000000002");
    public static readonly Guid Engineering = Guid.Parse("b1000000-0000-0000-0000-000000000001");
    public static readonly (Guid Id, string Name)[] Tenants = [(Northstar, "Northstar Studio"), (Meridian, "Meridian Labs")];

    private static readonly (string Name, string Domain, string Category, int Base)[] Catalog =
    [
        ("Slack", "slack.com", "Коммуникации", 860), ("Figma", "figma.com", "Дизайн", 740),
        ("GitHub", "github.com", "Разработка", 490), ("Notion", "notion.so", "База знаний", 320),
        ("Linear", "linear.app", "Управление проектами", 420), ("Google Workspace", "workspace.google.com", "Офис", 650),
        ("Datadog", "datadoghq.com", "Наблюдаемость", 1200), ("Sentry", "sentry.io", "Наблюдаемость", 185),
        ("1Password", "1password.com", "Безопасность", 155), ("Cloudflare", "cloudflare.com", "Инфраструктура", 240),
        ("Vercel", "vercel.com", "Инфраструктура", 160), ("Zoom", "zoom.us", "Коммуникации", 295),
        ("Miro", "miro.com", "Дизайн", 190), ("Asana", "asana.com", "Управление проектами", 275),
        ("Atlassian Cloud", "atlassian.com", "Управление проектами", 390), ("HubSpot", "hubspot.com", "CRM", 820),
        ("Intercom", "intercom.com", "Поддержка", 510), ("Zendesk", "zendesk.com", "Поддержка", 360),
        ("Calendly", "calendly.com", "Офис", 100), ("Dropbox Business", "dropbox.com", "Хранилище", 180),
        ("DocuSign", "docusign.com", "Документы", 120), ("Mixpanel", "mixpanel.com", "Аналитика", 250),
        ("Amplitude", "amplitude.com", "Аналитика", 210), ("Postman", "postman.com", "Разработка", 180),
        ("Docker Business", "docker.com", "Разработка", 255), ("Mural", "mural.co", "Дизайн", 105),
        ("Okta", "okta.com", "Безопасность", 500), ("Bitwarden", "bitwarden.com", "Безопасность", 150),
        ("Airtable", "airtable.com", "База знаний", 160), ("Freshdesk", "freshdesk.com", "Поддержка", 145),
        ("SendGrid", "sendgrid.com", "Коммуникации", 130), ("Grafana Cloud", "grafana.com", "Наблюдаемость", 195)
    ];

    public static async Task SeedAsync(string connection, TimeProvider clock, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var today = DateOnly.FromDateTime(clock.GetLocalNow().DateTime);
        foreach (var (tenant, name) in Tenants)
        {
            var actor = new Actor(tenant, "Demo import", "");
            await using var db = WorkspaceDb.Create(new DatabaseSettings(connection, false), actor);
            if (await db.Organizations.AnyAsync(ct)) continue;

            var northstar = tenant == Northstar;
            var departments = new[] {"Инженерия", "Продукт", "Операции", "Коммерция" }
                .Select(x => new Department { TenantId = tenant, Name = x }).ToArray();
            departments[0].Id = Engineering;
            db.Add(new Organization { TenantId = tenant, Id = tenant, Name = name });
            db.AddRange(departments);

            var services = (northstar ? Catalog.Take(26) : Catalog.Skip(26)).Select((x, i) =>
            {
                var cycle = i % 5 == 1 ? BillingCycle.Annual : i % 5 == 2 ? BillingCycle.Quarterly : BillingCycle.Monthly;
                return new Contract
                {
                    TenantId = tenant, DepartmentId = departments[i % 4].Id, Name = x.Name,
                    Domain = x.Domain, Category = x.Category, Cost = x.Base * (int)cycle,
                    Cycle = cycle, Currency = (Currency)(i % 4 == 0 ? 1 : i % 6 == 0 ? 2 : 0),
                    Seats = 12 + i % 12, AutoRenew = i % 9 != 8,
                    NoticeDays = i % 3 == 0 ? 14 : 30,
                    NextPayment = today.AddDays(i switch { 0 => 13, 1 => 32, 2 => 35, 3 => 20, 4 => 37, _ => 39 + i * 2 }),
                    ChangedBy = actor.Name
                };
            }).ToArray();
            db.AddRange(services);

            string[] people = northstar
                ? ["Анна Ким", "Максим Соколов", "Елена Волкова", "Илья Смирнов", "София Орлова", "Даниил Ли", "Майя Романова", "Артём Новиков", "Нина Белова", "Марк Кузнецов", "Алиса Попова", "Павел Егоров", "Ольга Власова", "Лев Иванов", "Ирина Пак", "Виктор Петров"]
                : ["Мария Тихонова", "Глеб Титов", "Кира Лебедева", "Вадим Яковлев", "Яна Алексеева", "Сергей Гусев"];
            var employees = people.Select((person, i) => new Employee
            {
                Name = person, TenantId = tenant, DepartmentId = departments[i % 4].Id,
                Job = i % 4 == 0 ? "Инженер" : i % 4 == 1 ? "Дизайнер" : "Специалист",
                Email = $"employee{i + 1}@{(northstar ? "northstar" : "meridian")}.example",
                Status = northstar && i == 1 ? EmployeeStatus.Offboarding : EmployeeStatus.Active
            }).ToArray();
            db.AddRange(employees);

            var grants = employees.SelectMany((person, i) => Enumerable.Range(0, northstar && i == 1 ? 3 : 4)
                .Select(offset => new AccessGrant
                {
                    TenantId = tenant, EmployeeId = person.Id,
                    ContractId = services[(i * 3 + offset) % services.Length].Id,
                    Role = offset == 0 ? AccessRole.Admin : AccessRole.Member,
                    Kind = AccessKind.Seat, ChangedBy = actor.Name, GrantedAt = now.AddDays(-80 + i),
                    LastUsedAt = offset % 3 == 0 ? now.AddDays(-43) : now.AddDays(-i)
                })).ToList();
            if (northstar)
            {
                var employee = employees[1];
                var key = new AccessGrant
                {
                    TenantId = tenant, EmployeeId = employee.Id, ContractId = services[2].Id,
                    Kind = AccessKind.ApiKey, Role = AccessRole.Owner, AssetLabel = "CI deploy token",
                    GrantedAt = now.AddDays(-60), ChangedBy = actor.Name
                };
                grants.Add(key);
                var run = new AccessRun
                {
                    TenantId = tenant, EmployeeId = employee.Id, Kind = RunKind.Offboarding,
                    StartedAt = now.AddHours(-5), StartedBy = "IT Security Admin"
                };
                foreach (var grant in grants.Where(x => x.EmployeeId == employee.Id))
                    run.Tasks.Add(new RunTask
                    {
                        TenantId = tenant, ContractId = grant.ContractId, AccessGrantId = grant.Id,
                        ServiceName = services.Single(x => x.Id == grant.ContractId).Name,
                        Kind = grant.Kind, Role = grant.Role, AssetLabel = grant.AssetLabel
                    });
                var first = run.Tasks[0];
                first.CompletedAt = now.AddHours(-3);
                first.CompletedBy = "IT Security Admin";
                first.Evidence = "IT-2041";
                grants.Single(x => x.Id == first.AccessGrantId).RevokedAt = first.CompletedAt;
                db.Add(run);
            }
            db.AddRange(grants);

            foreach (var (job, serviceIds) in new[] { ("Инженер", new[] { 0, 2, 3, 4 }), ("Дизайнер", new[] { 0, 1, 3 }), ("Менеджер продукта", new[] { 0, 1, 4 }) })
            {
                var template = new JobTemplate { TenantId = tenant, Name = job };
                template.Items.AddRange(serviceIds.Select(i => new TemplateItem
                {
                    TenantId = tenant, ContractId = services[i].Id, Role = AccessRole.Member
                }));
                db.Add(template);
            }

            db.AddRange(new FxRate { TenantId = tenant, Currency = Currency.EUR, EffectiveDate = today.AddDays(-3), UsdPerUnit = 1.08m },
                        new FxRate { TenantId = tenant, Currency = Currency.GBP, EffectiveDate = today.AddDays(-3), UsdPerUnit = 1.28m });
            db.Add(new PurchaseRequest
            {
                TenantId = tenant, DepartmentId = departments[0].Id, Name = northstar ? "Miro Enterprise" : "Pipedrive",
                Domain = northstar ? "miro.com" : "pipedrive.com", Category = northstar ? "Дизайн" : "CRM",
                Currency = Currency.USD, MonthlyCost = 260, State = PurchaseState.DepartmentReview,
                Reason = "Подготовка рабочего пространства для новой команды.", CreatedAt = now.AddDays(-2), CreatedBy = "Demo import"
            });

            await db.SaveChangesAsync(ct);

            // A separate transaction creates genuine SQL Server temporal history.
            services[0].Cost += northstar ? 40 : 12;
            services[0].ChangedBy = "CEO / FinOps";
            await db.SaveChangesAsync(ct);
        }
    }
}
