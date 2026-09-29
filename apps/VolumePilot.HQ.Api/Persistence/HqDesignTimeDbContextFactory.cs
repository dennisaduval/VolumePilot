using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace VolumePilot.HQ.Api.Persistence;

public sealed class HqDesignTimeDbContextFactory : IDesignTimeDbContextFactory<HqDbContext>
{
    public HqDbContext CreateDbContext(string[] args)
    {
        // Migrations can be scaffolded and scripted without opening a database connection.
        var options = new DbContextOptionsBuilder<HqDbContext>()
            .UseNpgsql()
            .Options;
        return new HqDbContext(options, new DesignTimeTenantContext());
    }

    private sealed class DesignTimeTenantContext : ITenantContext
    {
        public string? TenantId => null;
    }
}
