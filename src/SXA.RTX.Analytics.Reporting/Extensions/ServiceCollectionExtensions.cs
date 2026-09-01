using Microsoft.Extensions.DependencyInjection;
using SXA.RTX.Analytics.Reporting.Abstractions;
using SXA.RTX.Analytics.Reporting.Providers;

namespace SXA.RTX.Analytics.Reporting.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddReportingEngine(this IServiceCollection services)
    {
        services.AddScoped<IDataSourceProvider, SqlServerDataSourceProvider>();
        // OdbcDataSourceProvider will be registered here in a later phase.
        return services;
    }
}
