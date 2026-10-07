using Microsoft.Extensions.Options;

namespace SXA.RTX.Analytics.Web.Services;

/// <summary>
/// Resultado cacheado de la comprobacion de actualizaciones.
/// GitHub se consulta desde un unico hosted service, no desde cada navegador.
/// </summary>
public sealed record UpdateCheckResult(
    string Current,
    string? Latest,
    bool HasUpdate,
    string? Changelog,
    string? Url,
    DateTimeOffset CheckedAtUtc,
    string? Error);

public sealed class UpdateCheckOptions
{
    /// <summary>Minutos entre consultas a GitHub. 0 desactiva la comprobacion periodica.</summary>
    public int IntervalMinutes { get; set; } = 60;

    /// <summary>Usuario-Agent enviado a la API de GitHub.</summary>
    public string UserAgent { get; set; } = "SXA-RTX-Analytics";
}

/// <summary>
/// Mantiene en memoria el estado de "hay version nueva". Es un singleton para que
/// todas las sesiones compartan la misma consulta.
/// </summary>
public sealed class UpdateCheckCache
{
    public UpdateCheckResult? Current { get; set; }
}

/// <summary>
/// Consulta GitHub Releases periodicamente y llena <see cref="UpdateCheckCache"/>.
/// Si falla la red, se conserva el ultimo resultado correcto.
/// </summary>
public sealed class UpdateCheckWorker : BackgroundService
{
    private readonly UpdateCheckCache _cache;
    private readonly HttpClient _http;
    private readonly ILogger<UpdateCheckWorker> _logger;
    private readonly IConfiguration _config;
    private readonly UpdateCheckOptions _options;

    public UpdateCheckWorker(
        UpdateCheckCache cache,
        HttpClient http,
        IConfiguration config,
        IOptions<UpdateCheckOptions> options,
        ILogger<UpdateCheckWorker> logger)
    {
        _cache = cache;
        _http = http;
        _config = config;
        _options = options.Value;
        _logger = logger;
    }

    private static string CurrentVersion =>
        typeof(UpdateCheckWorker).Assembly.GetName().Version?.ToString() ?? "1.0.0";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await RefreshAsync(stoppingToken);
        var minutes = _options.IntervalMinutes;
        if (minutes <= 0)
        {
            _logger.LogInformation("Comprobacion periodica de actualizaciones desactivada");
            return;
        }
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(minutes));
        while (await timer.WaitForNextTickAsync(stoppingToken))
            await RefreshAsync(stoppingToken);
    }

    internal async Task RefreshAsync(CancellationToken ct)
    {
        var current = CurrentVersion;
        try
        {
            _http.DefaultRequestHeaders.UserAgent.Clear();
            _http.DefaultRequestHeaders.UserAgent.ParseAdd(_options.UserAgent);
            var latest = await _http.GetFromJsonAsync<GitHubRelease>(
                "https://api.github.com/repos/hector1516/SXA-RTX-Analytics/releases/latest", ct);
            if (latest is null) return;
            var hasUpdate = Version.TryParse(Normalize(current), out var c)
                         && Version.TryParse(Normalize(latest.tag_name), out var l)
                            ? c < l
                            : !string.Equals(Normalize(latest.tag_name), Normalize(current), StringComparison.OrdinalIgnoreCase);
            _cache.Current = new UpdateCheckResult(current, latest.tag_name, hasUpdate, latest.body, latest.html_url, DateTimeOffset.UtcNow, null);
            _logger.LogInformation("Actualizaciones: actual={Current} publicada={Latest} hay={HasUpdate}", current, latest.tag_name, hasUpdate);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception ex)
        {
            // Sin red: se conserva el ultimo resultado para no molestar al usuario.
            _logger.LogWarning(ex, "No se pudo comprobar actualizaciones en GitHub");
            _cache.Current ??= new UpdateCheckResult(current, null, false, null, null, DateTimeOffset.UtcNow, ex.Message);
        }
    }

    internal static string Normalize(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "0.0.0";
        var s = raw.Trim().TrimStart('v', 'V');
        var dash = s.IndexOfAny(new[] { '-', '+' });
        if (dash >= 0) s = s[..dash];
        var parts = s.Split('.', StringSplitOptions.RemoveEmptyEntries)
                     .Select(p => int.TryParse(p, out var n) ? n : 0)
                     .Take(4).ToList();
        while (parts.Count < 3) parts.Add(0);
        return string.Join('.', parts);
    }

    private sealed record GitHubRelease(string? tag_name, string? body, string? html_url);
}