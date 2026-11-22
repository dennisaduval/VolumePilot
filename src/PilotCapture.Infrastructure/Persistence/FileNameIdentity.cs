using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore;
using PilotCapture.Application.FastCapture;

namespace PilotCapture.Infrastructure.Persistence;

internal static class FileNameIdentity
{
    public static async Task<string> GetAsync(PilotCaptureDbContext db, string imageId, string station, CancellationToken token)
    {
        // Unique image key makes re-export and retries stable; SQLite owns allocation.
        var connection = db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open) await connection.OpenAsync(token);
        await using var command = connection.CreateCommand();
        command.Transaction = db.Database.CurrentTransaction?.GetDbTransaction();
        command.CommandText = "SELECT station,serial FROM capture_file_serials WHERE image_key=$id";
        var parameter = command.CreateParameter(); parameter.ParameterName = "$id"; parameter.Value = imageId; command.Parameters.Add(parameter);
        await using var reader = await command.ExecuteReaderAsync(token);
        if (!await reader.ReadAsync(token))
        {
            await reader.DisposeAsync();
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"INSERT OR IGNORE INTO capture_file_serials(image_key,station) VALUES({imageId},{station});", token);
            return await GetAsync(db, imageId, station, token);
        }
        return CaptureFileNames.Station(reader.GetString(0)) + "-" + CaptureFileNames.Serial(reader.GetInt64(1));
    }
}
