using Microsoft.Extensions.DependencyInjection;
using Nopds.Formats;

namespace Nopds.Scanner;

public static class DependencyInjection
{
    public static IServiceCollection AddNopdsScanner(this IServiceCollection services)
    {
        services.AddSingleton(_ => BookParsers.CreateDefault());
        services.AddScoped<LibraryScanner>();
        services.AddSingleton<ScanCoordinator>();
        services.AddHostedService(sp => sp.GetRequiredService<ScanCoordinator>());
        services.AddHostedService<ScanScheduler>();
        services.AddHostedService<LibraryWatcher>();
        return services;
    }
}
