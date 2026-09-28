using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Data.Sqlite;
using PilotCapture.Application.Rosters;

namespace PilotCapture.Infrastructure.Persistence;

public static class InfrastructureRegistration
{
    public static IServiceCollection AddPilotCaptureInfrastructure(
        this IServiceCollection services,
        string databasePath)
    {
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            Pooling = true
        }.ToString();

        services.AddDbContext<PilotCaptureDbContext>((_, options) =>
            options.UseSqlite(connectionString)
                .AddInterceptors(new SqliteConnectionPolicy()));
        services.AddScoped<DatabaseInitializer>();
        services.AddScoped<IRosterImportService, RosterImportService>();
        return services;
    }
}
