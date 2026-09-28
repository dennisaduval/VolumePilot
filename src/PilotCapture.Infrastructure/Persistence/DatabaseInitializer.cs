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
        if (currentVersion >= 2)
        {
            await EnsureLocalInstallationAsync(cancellationToken);
            return;
        }

        if (currentVersion < 1)
        {
            var migrationSql = await ReadMigrationAsync("0001_initial.sql", cancellationToken);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
            await using (var migrate = connection.CreateCommand())
            {
                migrate.Transaction = transaction;
                migrate.CommandText = migrationSql;
                await migrate.ExecuteNonQueryAsync(cancellationToken);
            }

            await MarkMigrationAsync(connection, transaction, 1, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            currentVersion = 1;
        }

        if (currentVersion < 2)
        {
            var migrationSql = await ReadMigrationAsync("0002_roster_import_rows.sql", cancellationToken);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
            await using (var migrate = connection.CreateCommand())
            {
                migrate.Transaction = transaction;
                migrate.CommandText = migrationSql;
                await migrate.ExecuteNonQueryAsync(cancellationToken);
            }

            await MarkMigrationAsync(connection, transaction, 2, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        await EnsureLocalInstallationAsync(cancellationToken);
    }

    private static async Task MarkMigrationAsync(
        System.Data.Common.DbConnection connection,
        System.Data.Common.DbTransaction transaction,
        int version,
        CancellationToken cancellationToken)
    {
        await using var mark = connection.CreateCommand();
        mark.Transaction = transaction;
        mark.CommandText = "INSERT INTO schema_migrations(version, applied_at_utc) VALUES ($version, $appliedAt);";
        var versionParameter = mark.CreateParameter();
        versionParameter.ParameterName = "$version";
        versionParameter.Value = version;
        mark.Parameters.Add(versionParameter);
        var timeParameter = mark.CreateParameter();
        timeParameter.ParameterName = "$appliedAt";
        timeParameter.Value = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);
        mark.Parameters.Add(timeParameter);
        await mark.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task EnsureLocalInstallationAsync(CancellationToken cancellationToken)
    {
        if (await dbContext.LocalInstallations.AnyAsync(cancellationToken))
            return;

        dbContext.LocalInstallations.Add(LocalInstallation.Create("s10", DateTimeOffset.UtcNow));
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static async Task<string> ReadMigrationAsync(string fileName, CancellationToken cancellationToken)
    {
        var assembly = typeof(DatabaseInitializer).Assembly;
        var resourceName = assembly.GetManifestResourceNames()
            .Single(x => x.EndsWith(fileName, StringComparison.Ordinal));
        await using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded migration resource '{resourceName}' was not found.");
        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync(cancellationToken);
    }
}
