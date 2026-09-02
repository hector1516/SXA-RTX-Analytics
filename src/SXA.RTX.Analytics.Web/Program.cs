using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Serilog;
using SXA.RTX.Analytics.Domain.Entities;
using SXA.RTX.Analytics.Infrastructure.Extensions;
using SXA.RTX.Analytics.Infrastructure.Persistence;
using SXA.RTX.Analytics.Reporting.Extensions;
using SXA.RTX.Analytics.Web.Components;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((ctx, services, cfg) =>
{
    cfg.ReadFrom.Configuration(ctx.Configuration)
       .ReadFrom.Services(services)
       .Enrich.FromLogContext()
       .Enrich.WithProperty("Application", "SXA.RTX.Analytics")
       .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
       .WriteTo.File(path: "logs/sxa-rtx-analytics-.log", rollingInterval: RollingInterval.Day, retainedFileCountLimit: 14,
            outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj} {Properties:j}{NewLine}{Exception}");
});

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddHttpContextAccessor();
builder.Services.AddCascadingAuthenticationState();

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(opt =>
    {
        opt.LoginPath = "/login";
        opt.LogoutPath = "/logout";
        opt.AccessDeniedPath = "/login";
        opt.ExpireTimeSpan = TimeSpan.FromHours(8);
        opt.SlidingExpiration = true;
        opt.Cookie.Name = "SXA_RTX_Auth";
        opt.Cookie.HttpOnly = true;
    });
builder.Services.AddAuthorization(opts =>
{
    opts.AddPolicy("AdminOnly", p => p.RequireRole("Administrador"));
});

builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddReportingEngine();

builder.Services.AddHealthChecks()
    .AddDbContextCheck<ConfigurationDbContext>(name: "configuration_database", failureStatus: HealthStatus.Degraded, tags: ["db", "configuration"])
    .AddCheck("self", () => HealthCheckResult.Healthy("Application is running"), tags: ["live"]);

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();
app.UseAntiforgery();
app.UseAuthentication();
app.UseAuthorization();
app.MapStaticAssets();

app.MapHealthChecks("/health", new HealthCheckOptions
{
    Predicate = _ => true,
    ResponseWriter = async (ctx, report) =>
    {
        ctx.Response.ContentType = "application/json; charset=utf-8";
        var json = System.Text.Json.JsonSerializer.Serialize(new { status = report.Status.ToString(), duration = report.TotalDuration.ToString(), checks = report.Entries.Select(e => new { name = e.Key, status = e.Value.Status.ToString(), duration = e.Value.Duration.ToString(), description = e.Value.Description, data = e.Value.Data }) }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        await ctx.Response.WriteAsync(json);
    }
});
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = r => r.Tags.Contains("live"), ResponseWriter = async (ctx, report) => { ctx.Response.ContentType = "text/plain"; await ctx.Response.WriteAsync(report.Status == HealthStatus.Healthy ? "Healthy" : "Unhealthy"); } });

app.MapGet("/logout", async (HttpContext ctx) =>
{
    await ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    ctx.Response.Redirect("/login");
    return Results.Empty;
});

app.MapPost("/api/auth/login", async (HttpContext ctx, SXA.RTX.Analytics.Application.Abstractions.IAuthService auth, LoginRequest req) =>
{
    var res = await auth.LoginAsync(req.Username, req.Password);
    if (!res.Success || res.User is null) return Results.Json(new { success = false, message = res.Message }, statusCode: 401);
    var claims = new List<System.Security.Claims.Claim>
    {
        new(System.Security.Claims.ClaimTypes.Name, res.User.Username),
        new(System.Security.Claims.ClaimTypes.NameIdentifier, res.User.Id.ToString()),
        new(System.Security.Claims.ClaimTypes.Role, res.User.Role.ToString()),
        new("DisplayName", res.User.DisplayName),
    };
    var identity = new System.Security.Claims.ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
    var principal = new System.Security.Claims.ClaimsPrincipal(identity);
    await ctx.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal, new Microsoft.AspNetCore.Authentication.AuthenticationProperties { IsPersistent = true, ExpiresUtc = DateTimeOffset.UtcNow.AddHours(8) });
    return Results.Json(new { success = true });
}).DisableAntiforgery();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

try
{
    Log.Information("Starting SXA-RTX Analytics Web (Environment={Environment})", app.Environment.EnvironmentName);
    using (var scope = app.Services.CreateScope())
    {
        var db = scope.ServiceProvider.GetRequiredService<ConfigurationDbContext>();
        if (db.Database.IsRelational()) await db.Database.EnsureCreatedAsync();
        else await db.Database.EnsureCreatedAsync();

        // Seed Administrador ECCSA / Qwe123456 si no existe
        if (!await db.Set<AppUser>().AnyAsync(x => x.Username == "ECCSA"))
        {
            var hash = BCrypt.Net.BCrypt.HashPassword("Qwe123456");
            db.Set<AppUser>().Add(new AppUser { Username = "ECCSA", PasswordHash = hash, DisplayName = "ECCSA", Role = AppRole.Administrador });
            await db.SaveChangesAsync();
            Log.Information("Seed usuario ECCSA creado");
        }
        Log.Information("DB ready Provider={Provider} Users={Count}", db.Database.ProviderName, await db.Set<AppUser>().CountAsync());
    }
}
catch (Exception ex) { Log.Fatal(ex, "Startup failed"); throw; }

app.Lifetime.ApplicationStarted.Register(() => Log.Information("SXA-RTX Analytics started successfully"));
app.Lifetime.ApplicationStopping.Register(() => Log.Information("SXA-RTX Analytics stopping"));

app.Run();

record LoginRequest(string Username, string Password);
