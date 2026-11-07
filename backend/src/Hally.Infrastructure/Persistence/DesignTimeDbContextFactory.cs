using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Hally.Infrastructure.Persistence;

/// <summary>Used only by the dotnet-ef CLI to build migrations.</summary>
public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<HallyDbContext>
{
    public HallyDbContext CreateDbContext(string[] args)
    {
        var cs = Environment.GetEnvironmentVariable("HALLY_DESIGN_CONNECTION")
                 ?? "Host=localhost;Database=hally;Username=postgres;Password=postgres";
        var options = new DbContextOptionsBuilder<HallyDbContext>()
            .UseNpgsql(cs)
            .UseSnakeCaseNamingConvention()
            .Options;
        return new HallyDbContext(options);
    }
}
