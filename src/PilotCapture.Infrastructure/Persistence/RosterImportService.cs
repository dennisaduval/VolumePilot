using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using PilotCapture.Application.Rosters;
using PilotCapture.Domain;

namespace PilotCapture.Infrastructure.Persistence;

public sealed class RosterImportService(PilotCaptureDbContext dbContext) : IRosterImportService
{
    public async Task<RosterImportResult> ImportAsync(
        RosterImportCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (string.IsNullOrWhiteSpace(command.EventName))
            throw new ArgumentException("An event name is required.", nameof(command));
        if (command.EventName.Trim().Length > 200)
            throw new ArgumentException("Event names can be at most 200 characters.", nameof(command));
        if (string.IsNullOrWhiteSpace(command.SourceFileName) || command.SourceFileName.Length > 255)
            throw new ArgumentException("A source file name of at most 255 characters is required.", nameof(command));
        if (command.SourceSha256.Length != 64 || !command.SourceSha256.All(Uri.IsHexDigit))
            throw new ArgumentException("The source file SHA-256 must contain 64 hexadecimal characters.", nameof(command));

        var now = DateTimeOffset.UtcNow;
        var captureEvent = new Event { Name = command.EventName.Trim(), CreatedAtUtc = now };
        var import = new RosterImport
        {
            EventId = captureEvent.Id,
            SourceFileName = command.SourceFileName,
            SourceSha256 = command.SourceSha256.ToLowerInvariant(),
            ImportedAtUtc = now,
            RowsRead = command.Rows.Count
        };
        var groups = new Dictionary<string, Group>(StringComparer.OrdinalIgnoreCase);
        var subjects = new Dictionary<string, Subject>(StringComparer.Ordinal);
        var memberships = new Dictionary<(string SubjectId, string GroupId), Membership>();
        var importRows = new List<RosterImportRow>(command.Rows.Count);
        var skipped = 0;

        foreach (var row in command.Rows)
        {
            Membership? membership = null;
            var groupName = row.GroupName?.Trim();
            var hasName = command.AllowDisplayNameOnly
                ? !string.IsNullOrWhiteSpace(row.DisplayName)
                : !string.IsNullOrWhiteSpace(row.FirstName) && !string.IsNullOrWhiteSpace(row.LastName);
            var placeholder = command.AllowDisplayNameOnly && !hasName &&
                !string.IsNullOrWhiteSpace(row.ClassOrCategory);
            if ((!hasName && !placeholder) || string.IsNullOrWhiteSpace(groupName))
            {
                skipped++;
            }
            else
            {
                if (!groups.TryGetValue(groupName, out var group))
                {
                    group = new Group
                    {
                        EventId = captureEvent.Id,
                        Name = groupName,
                        CreatedAtUtc = now,
                        IsActive = true
                    };
                    groups.Add(groupName, group);
                }

                var subjectKey = !placeholder && row.PotentialMatchKey is { } key && command.ConfirmedMatchKeys.Contains(key)
                    ? "match:" + key
                    : "row:" + row.SourceRecordNumber.ToString(System.Globalization.CultureInfo.InvariantCulture);
                if (!subjects.TryGetValue(subjectKey, out var subject))
                {
                    var firstName = row.FirstName?.Trim();
                    var lastName = row.LastName?.Trim();
                    subject = new Subject
                    {
                        EventId = captureEvent.Id,
                        FirstName = firstName,
                        LastName = lastName,
                        IdentityStatus = placeholder ? SubjectIdentityStatus.Unidentified : SubjectIdentityStatus.Known,
                        DisplayName = placeholder ? $"{row.ClassOrCategory} (unnamed · row {row.SourceRecordNumber})"
                            : command.AllowDisplayNameOnly ? row.DisplayName : $"{firstName} {lastName}",
                        CreatedAtUtc = now,
                        IsActive = true
                    };
                    subjects.Add(subjectKey, subject);
                }

                var membershipKey = (subject.Id, group.Id);
                if (!memberships.TryGetValue(membershipKey, out membership))
                {
                    membership = new Membership
                    {
                        SubjectId = subject.Id,
                        GroupId = group.Id,
                        RosterImportId = import.Id,
                        RosterNumber = row.RosterNumber,
                        Role = row.ClassOrCategory,
                        SourceRowKey = row.SourceRecordNumber.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        SourceDataJson = row.SourceDataJson,
                        SpaDataJson = placeholder ? UnnamedSpaData(row) : row.SpaDataJson,
                        CreatedAtUtc = now,
                        IsActive = true
                    };
                    memberships.Add(membershipKey, membership);
                }
            }

            importRows.Add(new RosterImportRow
            {
                RosterImportId = import.Id,
                SourceRecordNumber = row.SourceRecordNumber,
                MembershipId = membership?.Id,
                SourceDataJson = row.SourceDataJson
            });
        }

        import.RowsImported = importRows.Count(row => row.MembershipId is not null);
        import.RowsSkipped = skipped;
        dbContext.Events.Add(captureEvent);
        dbContext.RosterImports.Add(import);
        dbContext.Groups.AddRange(groups.Values);
        dbContext.Subjects.AddRange(subjects.Values);
        dbContext.Memberships.AddRange(memberships.Values);
        dbContext.RosterImportRows.AddRange(importRows);
        await dbContext.SaveChangesAsync(cancellationToken);

        return new RosterImportResult(captureEvent.Id, import.Id, import.RowsRead, import.RowsImported, import.RowsSkipped);
    }

    private static string UnnamedSpaData(RosterPreviewRow row)
    {
        var fields = row.SpaDataJson is { } json
            ? System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(json)!
            : SpaRosterFields.ReadSource(row.SourceDataJson);
        fields["NAME"] = "";
        return System.Text.Json.JsonSerializer.Serialize(fields);
    }

    public static string ComputeSha256(ReadOnlySpan<byte> content) => Convert.ToHexString(SHA256.HashData(content));
}

