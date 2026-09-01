using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Serilog;
using SXA.RTX.Analytics.Infrastructure.Extensions;
using SXA.RTX.Analytics.Infrastructure.Persistence;
using SXA.RTX.Analytics.Reporting.Extensions;
using SXA.RTX.Analytics.Web.Components;

var builder = WebApplication.CreateBuilder(args);

// ------------------------------------------------------------------
// Serilog — structured logging (console + file, no secrets)
// ------------------------------------------------------------------
builder.Host.UseSerilog((ctx, services, cfg) =>
{
    cfg.ReadFrom.Configuration(ctx.Configuration)
       .ReadFrom.Services(services)
       .Enrich.FromLogContext()
       .Enrich.WithProperty("Application", "SXA.RTX.Analytics")
       .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
       .WriteTo.File(
            path: "logs/sxa-rtx-analytics-.log",
            rollingInterval: RollingInterval.Day,
            retainedFileCountLimit: 14,
            outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj} {Properties:j}{NewLine}{Exception}");
});

// ------------------------------------------------------------------
// Configuration
// ------------------------------------------------------------------
// ConnectionStrings:ConfigurationDatabase is read from appsettings.json / env / user-secrets.
// Never commit real secrets. See docs/SECURITY.md.

// ------------------------------------------------------------------
// Services
// ------------------------------------------------------------------
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddReportingEngine();

builder.Services.AddHealthChecks()
    .AddDbContextCheck<ConfigurationDbContext>(
        name: "configuration_database",
        failureStatus: HealthStatus.Degraded,
        tags: ["db", "configuration"])
    .AddCheck("self", () => HealthCheckResult.Healthy("Application is running"), tags: ["live"]);

var app = builder.Build();

// ------------------------------------------------------------------
// Request pipeline
// ------------------------------------------------------------------
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();
app.UseAntiforgery();
app.MapStaticAssets();

// ------------------------------------------------------------------
// Health endpoints
// ------------------------------------------------------------------
app.MapHealthChecks("/health", new HealthCheckOptions
{
    Predicate = _ => true,
    ResponseWriter = async (ctx, report) =>
    {
        ctx.Response.ContentType = "application/json; charset=utf-8";
        var json = System.Text.Json.JsonSerializer.Serialize(new
        {
            status = report.Status.ToString(),
            duration = report.TotalDuration.ToString(),
            checks = report.Entries.Select(e => new
            {
                name = e.Key,
                status = e.Value.Status.ToString(),
                duration = e.Value.Duration.ToString(),
                description = e.Value.Description,
                data = e.Value.Data
            })
        }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        await ctx.Response.WriteAsync(json);
    }
});

app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = r => r.Tags.Contains("live"),
    ResponseWriter = async (ctx, report) =>
    {
        ctx.Response.ContentType = "text/plain";
        await ctx.Response.WriteAsync(report.Status == HealthStatus.Healthy ? "Healthy" : "Unhealthy");
    }
});

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

// ------------------------------------------------------------------
// Startup logging + ensure DB created (Phase 1 scaffold)
// ------------------------------------------------------------------
try
{
    Log.Information("Starting SXA-RTX Analytics Web (Environment={Environment})", app.Environment.EnvironmentName);

    // Auto-create / migrate the configuration database if a real connection string is present.
    // For InMemory fallback this is a no-op (DB is created on first use).
    using (var scope = app.Services.CreateScope())
    {
        var db = scope.ServiceProvider.GetRequiredService<ConfigurationDbContext>();
        if (db.Database.IsRelational())
        {
            // EnsureCreated is safe for scaffold; will be replaced by migrations once SQL Server is available.
            // Comment out if you prefer explicit `dotnet ef migrations` workflow.
            await db.Database.EnsureCreatedAsync();
            Log.Information("Configuration database ensured (Provider={Provider})", db.Database.ProviderName);
        }
        else
        {
            await db.Database.EnsureCreatedAsync();
            Log.Information("Using in-memory configuration store (no ConnectionStrings:ConfigurationDatabase)");
        }
    }
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application startup failed");
    throw;
}

app.Lifetime.ApplicationStarted.Register(() => Log.Information("SXA-RTX Analytics started successfully"));
app.Lifetime.ApplicationStopping.Register(() => Log.Information("SXA-RTX Analytics stopping"));

app.Run();
