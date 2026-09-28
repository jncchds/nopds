using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Nopds.Infrastructure.Data;

/// <summary>Used by dotnet-ef to create migrations without starting the web host.</summary>
public sealed class DesignTimeFactory : IDesignTimeDbContextFactory<NopdsDbContext>
{
    public NopdsDbContext CreateDbContext(string[] args)
    {
        var cs = Environment.GetEnvironmentVariable("NOPDS_DESIGN_CONNECTION")
                 ?? "Host=localhost;Database=nopds;Username=nopds;Password=nopds";
        var builder = new DbContextOptionsBuilder<NopdsDbContext>();
        DbSetup.Configure(builder, cs);
        return new NopdsDbContext(builder.Options);
    }
}
