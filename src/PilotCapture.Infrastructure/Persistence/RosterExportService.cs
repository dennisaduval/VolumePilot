using System.Text;
using Microsoft.EntityFrameworkCore;
using PilotCapture.Application.Rosters;

namespace PilotCapture.Infrastructure.Persistence;

public sealed class RosterExportService(PilotCaptureDbContext dbContext) : IRosterExportService
{
    private static readonly string[] Headers =
    [
        "Event", "Subject ID", "Membership ID", "First Name", "Last Name",
        "Display Name", "Identity Status", "Group", "Roster Number", "Role", "Record Source"
    ];

    public async Task<int> ExportEventAsync(
        string eventId,
        Stream destination,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventId);
        ArgumentNullException.ThrowIfNull(destination);
        if (!destination.CanWrite)
            throw new ArgumentException("The export destination must be writable.", nameof(destination));

        var subjects = await dbContext.Subjects.AsNoTracking()
            .Where(subject => subject.EventId == eventId && subject.IsActive)
            .Include(subject => subject.Event)
            .Include(subject => subject.Memberships)
                .ThenInclude(membership => membership.Group)
            .Include(subject => subject.Memberships)
                .ThenInclude(membership => membership.SourceImport)
            .ToListAsync(cancellationToken);

        var records = new List<RosterExportRow>();
        foreach (var subject in subjects.OrderBy(item => item.LastName).ThenBy(item => item.FirstName).ThenBy(item => item.Id))
        {
            var memberships = subject.Memberships
                .Where(membership => membership.IsActive && membership.Group is { IsActive: true })
                .OrderBy(membership => membership.Group!.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(membership => membership.Id)
                .ToArray();
            if (memberships.Length == 0)
            {
                records.Add(new RosterExportRow(
                    subject.Event!.Name, subject.Id, null, subject.FirstName, subject.LastName,
                    subject.DisplayName, subject.IdentityStatus.ToString(), null, null, null, "Local subject"));
                continue;
            }

            foreach (var membership in memberships)
            {
                records.Add(new RosterExportRow(
                    subject.Event!.Name,
                    subject.Id,
                    membership.Id,
                    subject.FirstName,
                    subject.LastName,
                    subject.DisplayName,
                    subject.IdentityStatus.ToString(),
                    membership.Group!.Name,
                    membership.RosterNumber,
                    membership.Role,
                    membership.RosterImportId is null ? "Photographer entered" : "Imported roster"));
            }
        }

        await using var writer = new StreamWriter(
            destination, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true), 64 * 1024, leaveOpen: true);
        await writer.WriteLineAsync(string.Join(",", Headers.Select(Escape)));
        foreach (var row in records)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var values = new string?[]
            {
                row.EventName, row.SubjectId, row.MembershipId, row.FirstName, row.LastName,
                row.DisplayName, row.IdentityStatus, row.GroupName, row.RosterNumber, row.Role, row.RecordSource
            };
            await writer.WriteLineAsync(string.Join(",", values.Select(Escape)));
        }

        await writer.FlushAsync(cancellationToken);
        return records.Count;
    }

    private static string Escape(string? value)
    {
        value ??= string.Empty;
        return value.IndexOfAny([',', '"', '\r', '\n']) >= 0
            ? $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\""
            : value;
    }
}
