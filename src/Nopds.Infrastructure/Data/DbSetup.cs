using Microsoft.EntityFrameworkCore;

namespace Nopds.Infrastructure.Data;

public static class DbSetup
{
    public static void Configure(DbContextOptionsBuilder builder, string connectionString)
    {
        builder
            .UseNpgsql(connectionString, o =>
            {
                o.MigrationsHistoryTable("__ef_migrations");
                o.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery);
            })
            .UseSnakeCaseNamingConvention();
    }
}
