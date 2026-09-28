using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Data.Sqlite;
using PilotCapture.Application;
using PilotCapture.Application.Rosters;
using PilotCapture.Application.Capture;
using PilotCapture.Infrastructure.Files;

namespace PilotCapture.Infrastructure.Persistence;

public static class InfrastructureRegistration
{
    public static IServiceCollection AddPilotCaptureInfrastructure(
        this IServiceCollection services,
        string databasePath,
        string mediaRootPath)
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
        services.AddScoped<ICaptureWorkflowService, CaptureWorkflowService>();
        services.AddSingleton<IImageAssetStore>(_ => new FileSystemImageAssetStore(mediaRootPath));
        services.AddScoped<ICaptureDataStore, CaptureDataStore>();
        services.AddScoped<IImageIngestService, ImageIngestService>();
        services.AddScoped<IImageReviewService, ImageReviewService>();
        services.AddScoped<IImageAssociationExportService, ImageAssociationExportService>();
        return services;
    }
}
