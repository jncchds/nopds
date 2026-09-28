using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nopds.Infrastructure.Data;
using Nopds.Infrastructure.Genres;
using Nopds.Infrastructure.Identity;
using Nopds.Infrastructure.Settings;

namespace Nopds.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddNopdsInfrastructure(this IServiceCollection services, string connectionString)
    {
        services.AddPooledDbContextFactory<NopdsDbContext>(o => DbSetup.Configure(o, connectionString));
        services.AddScoped(sp => sp.GetRequiredService<IDbContextFactory<NopdsDbContext>>().CreateDbContext());

        services.AddIdentityCore<AppUser>(o =>
            {
                o.Password.RequireDigit = false;
                o.Password.RequireNonAlphanumeric = false;
                o.Password.RequireUppercase = false;
                o.Password.RequireLowercase = false;
                o.Password.RequiredLength = 6;
                o.User.RequireUniqueEmail = false;
                o.Lockout.MaxFailedAccessAttempts = 10;
                o.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(5);
            })
            .AddEntityFrameworkStores<NopdsDbContext>();

        services.AddSingleton(_ => GenreCatalog.Load());
        services.AddSingleton<SettingsStore>();
        services.AddScoped<DatabaseInitializer>();
        services.AddScoped<Browse.CatalogService>();
        return services;
    }
}
