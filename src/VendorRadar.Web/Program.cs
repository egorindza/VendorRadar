using System.Globalization;
using System.Net;
using System.Security.Cryptography.X509Certificates;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Infrastructure;
using VendorRadar.Application;
using VendorRadar.Infrastructure;
using VendorRadar.Web;

if (args is ["--hash-password"]) { Environment.ExitCode = AccountPasswordCommand.Run(); return; }

var builder = WebApplication.CreateBuilder(args);
var configuration = builder.Configuration;
var local = builder.Environment.IsDevelopment();
var connection = configuration.GetConnectionString("VendorRadar")
    ?? throw new InvalidOperationException("Configure ConnectionStrings:VendorRadar.");
var dbSettings = new DatabaseSettings(connection, local && configuration.GetValue<bool>("Database:ImpersonateLocalUser"));
var directory = new WorkspaceDirectory(configuration, local);
builder.Services.AddSingleton(directory);
builder.Services.AddSingleton(dbSettings);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentActor, HttpActor>();
builder.Services.AddScoped<TenantSessionInterceptor>();
builder.Services.AddDbContext<RadarDb>((services, options) => options.UseSqlServer(dbSettings.ScopedConnectionString)
    .AddInterceptors(services.GetRequiredService<TenantSessionInterceptor>()));
builder.Services.AddScoped<IAnalytics, Analytics>();
builder.Services.AddScoped<AccessWorkflows>();
builder.Services.AddScoped<Purchasing>();
builder.Services.AddScoped<ContractRegistry>();
builder.Services.AddSingleton<PdfReports>(services => new PdfReports(services.GetRequiredService<X509Certificate2>()));
builder.Services.AddSingleton(_ =>
{
    var path = configuration["Pdf:CertificatePath"];
    if (string.IsNullOrWhiteSpace(path))
        return local ? PdfReports.DevelopmentCertificate() :
            throw new InvalidOperationException("Configure Pdf:CertificatePath for signed closure reports.");
    var cert = X509CertificateLoader.LoadPkcs12FromFile(path, configuration["Pdf:CertificatePassword"], X509KeyStorageFlags.EphemeralKeySet);
    if (cert.HasPrivateKey) return cert;
    cert.Dispose();
    throw new InvalidOperationException("PDF signing certificate requires a private key.");
});
QuestPDF.Settings.License = configuration.GetValue<LicenseType>("Pdf:License");
builder.Services.AddControllersWithViews(options => options.Filters.Add(new AutoValidateAntiforgeryTokenAttribute()));
builder.Services.AddSignalR();
builder.Services.AddSingleton<IWorkspaceEvents, SignalEvents>();
builder.Services.AddHostedService<RadarWorker>();
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(options =>
{
    options.LoginPath = "/account/login";
    options.AccessDeniedPath = "/account/denied";
    options.Cookie.Name = "VendorRadar.Auth";
    options.Cookie.SecurePolicy = local ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.ExpireTimeSpan = TimeSpan.FromHours(8);
    options.Events.OnValidatePrincipal = CookieSessions.ValidateAsync;
    options.Events.OnRedirectToLogin = CookieSessions.ChallengeAsync;
});
builder.Services.AddAuthorization();
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = 429;
    options.AddPolicy("login", context => RateLimitPartition.GetFixedWindowLimiter(
        (context.Connection.RemoteIpAddress ?? IPAddress.None).ToString(),
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 12, Window = TimeSpan.FromMinutes(1) }));
});

var app = builder.Build();
if (!local) _ = app.Services.GetRequiredService<PdfReports>();
if (local && configuration.GetValue<bool>("Database:Initialize"))
{
    var bootstrap = configuration.GetConnectionString("VendorRadarAdmin") ?? connection;
    await WorkspaceDb.MigrateAsync(bootstrap, app.Lifetime.ApplicationStopping);
    if (directory.DemoEnabled && configuration.GetValue<bool>("Demo:Seed"))
        await DemoData.SeedAsync(bootstrap, TimeProvider.System, app.Lifetime.ApplicationStopping);
}
var culture = CultureInfo.GetCultureInfo("ru-RU");
app.UseRequestLocalization(new RequestLocalizationOptions
{
    DefaultRequestCulture = new("en-US", "ru-RU"),
    SupportedCultures = [culture, CultureInfo.GetCultureInfo("en-US")],
    SupportedUICultures = [culture],
    RequestCultureProviders = []
});
if (!local)
{
    app.UseExceptionHandler("/error");
    app.UseHsts();
    app.UseHttpsRedirection();
}
app.Use(async (context, next) =>
{
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["Referrer-Policy"] = "same-origin";
    context.Response.Headers["Content-Security-Policy"] =
        "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; frame-ancestors 'none'; form-action 'self'; base-uri 'none'";
    if (context.Request.Path.StartsWithSegments("/realtime") &&
        context.Request.Headers.TryGetValue("Origin", out var origin) &&
        !string.Equals(origin, $"{context.Request.Scheme}://{context.Request.Host}", StringComparison.OrdinalIgnoreCase))
    {
        context.Response.StatusCode = 403;
        return;
    }
    await next();
});
app.UseStaticFiles();
app.UseRouting();
app.UseRateLimiter();
app.UseAuthentication();
app.Use(async (context, next) =>
{
    context.Response.OnStarting(() =>
    {
        if (!context.Response.Headers.ContainsKey("Cache-Control"))
            context.Response.Headers.CacheControl = "no-store";
        return Task.CompletedTask;
    });
    await next();
});
app.UseAuthorization();
app.MapControllers();
app.MapHub<WorkspaceHub>("/realtime", options => options.CloseOnAuthenticationExpiration = true).RequireAuthorization();
app.Run();
