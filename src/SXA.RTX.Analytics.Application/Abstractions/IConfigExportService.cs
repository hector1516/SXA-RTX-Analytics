namespace SXA.RTX.Analytics.Application.Abstractions;

public interface IConfigExportService
{
    Task<string> ExportJsonAsync(CancellationToken ct = default);
    Task<(bool Success, string Message)> ImportJsonAsync(string json, CancellationToken ct = default);
}
