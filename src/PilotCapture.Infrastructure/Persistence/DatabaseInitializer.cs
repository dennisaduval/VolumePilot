using System.Globalization;
using Microsoft.EntityFrameworkCore;
using PilotCapture.Domain;

namespace PilotCapture.Infrastructure.Persistence;

public sealed class DatabaseInitializer(PilotCaptureDbContext dbContext)
{
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        var connection = dbContext.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
            await connection.OpenAsync(cancellationToken);

        await using (var pragma = connection.CreateCommand())
        {
            pragma.CommandText = "PRAGMA journal_mode = WAL; PRAGMA foreign_keys = ON; PRAGMA busy_timeout = 5000;";
            await pragma.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var createHistory = connection.CreateCommand())
        {
            createHistory.CommandText = "CREATE TABLE IF NOT EXISTS schema_migrations (version INTEGER NOT NULL PRIMARY KEY, applied_at_utc TEXT NOT NULL);";
            await createHistory.ExecuteNonQueryAsync(cancellationToken);
        }

        await using var readVersion = connection.CreateCommand();
        readVersion.CommandText = "SELECT COALESCE(MAX(version), 0) FROM schema_migrations;";
        var currentVersion = Convert.ToInt32(await readVersion.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);
        if (currentVersion >= 1)
        {
            await EnsureLocalInstallationAsync(cancellationToken);
            return;
        }

        var migrationSql = await ReadInitialMigrationAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using (var migrate = connection.CreateCommand())
        {
            migrate.Transaction = transaction;
            migrate.CommandText = migrationSql;
            await migrate.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var mark = connection.CreateCommand())
        {
            mark.Transaction = transaction;
            mark.CommandText = "INSERT INTO schema_migrations(version, applied_at_utc) VALUES (1, $appliedAt);";
            var parameter = mark.CreateParameter();
            parameter.ParameterName = "$appliedAt";
            parameter.Value = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);
            mark.Parameters.Add(parameter);
            await mark.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        await EnsureLocalInstallationAsync(cancellationToken);
    }

    private async Task EnsureLocalInstallationAsync(CancellationToken cancellationToken)
    {
        if (await dbContext.LocalInstallations.AnyAsync(cancellationToken))
            return;

        dbContext.LocalInstallations.Add(LocalInstallation.Create("s10", DateTimeOffset.UtcNow));
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static async Task<string> ReadInitialMigrationAsync(CancellationToken cancellationToken)
    {
        var assembly = typeof(DatabaseInitializer).Assembly;
        var resourceName = assembly.GetManifestResourceNames()
            .Single(x => x.EndsWith("0001_initial.sql", StringComparison.Ordinal));
        await using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded migration resource '{resourceName}' was not found.");
        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync(cancellationToken);
    }
}
