using System.Text;

namespace PilotCapture.Application.Rosters;

/// <summary>Suggested columns inferred from common roster headers. Suggestions never imply identity.</summary>
public sealed record RosterColumnSuggestions(
    int? FirstNameColumn,
    int? LastNameColumn,
    int? TeamOrSchoolColumn,
    int? SportOrGroupColumn,
    int? RosterNumberColumn,
    int? ClassOrCategoryColumn,
    IReadOnlyList<string> Warnings);

public static class RosterColumnSuggestionBuilder
{
    public static RosterColumnSuggestions Build(IReadOnlyList<string> headers)
    {
        ArgumentNullException.ThrowIfNull(headers);

        var warnings = new List<string>();
        var firstName = Find(headers, "firstname", "givenname");
        var lastName = Find(headers, "lastname", "familyname", "surname");
        var teamOrSchool = Find(headers, "teamschool", "teamname", "groupname", "schoolname", "team", "school");
        var sportOrGroup = Find(headers, "sport", "discipline", "group");
        var number = Find(headers, "number", "rosternumber", "jerseynumber", "jersey");
        var category = Find(headers, "class", "grade", "classification", "role", "position");

        if (firstName is null || lastName is null)
            warnings.Add("Map first and last name columns before importing subjects.");
        if (teamOrSchool is null && sportOrGroup is null)
            warnings.Add("Map at least one team, school, sport, or group column before importing memberships.");
        if (headers.Select(NormalizeHeader).Where(x => x.Length > 0).GroupBy(x => x, StringComparer.Ordinal).Any(x => x.Count() > 1))
            warnings.Add("The file has duplicate header names; review the column mapping before importing.");

        return new RosterColumnSuggestions(firstName, lastName, teamOrSchool, sportOrGroup, number, category, warnings);
    }

    private static int? Find(IReadOnlyList<string> headers, params string[] aliases)
    {
        var accepted = aliases.ToHashSet(StringComparer.Ordinal);
        for (var index = 0; index < headers.Count; index++)
        {
            if (accepted.Contains(NormalizeHeader(headers[index])))
                return index;
        }
        return null;
    }

    private static string NormalizeHeader(string header) =>
        string.Concat(header.Where(char.IsLetterOrDigit)).ToLowerInvariant();
}

/// <summary>Explicit column mapping chosen or confirmed by the operator for a roster preview.</summary>
public sealed record RosterColumnMapping(
    int? FirstNameColumn,
    int? LastNameColumn,
    int? TeamOrSchoolColumn,
    int? SportOrGroupColumn,
    int? RosterNumberColumn,
    int? ClassOrCategoryColumn);

public sealed record RosterPreviewRow(
    int SourceRecordNumber,
    string? FirstName,
    string? LastName,
    string DisplayName,
    string? GroupName,
    string? RosterNumber,
    string? ClassOrCategory,
    int PotentialCrossGroupNameMatchCount,
    bool HasExpectedFieldCount,
    string SourceDataJson);

public static class RosterImportPreviewBuilder
{
    public static IReadOnlyList<RosterPreviewRow> Build(
        RosterCsvDocument document,
        RosterColumnMapping mapping)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(mapping);

        ValidateColumn(mapping.FirstNameColumn, document.Headers.Count, nameof(mapping.FirstNameColumn));
        ValidateColumn(mapping.LastNameColumn, document.Headers.Count, nameof(mapping.LastNameColumn));
        ValidateColumn(mapping.TeamOrSchoolColumn, document.Headers.Count, nameof(mapping.TeamOrSchoolColumn));
        ValidateColumn(mapping.SportOrGroupColumn, document.Headers.Count, nameof(mapping.SportOrGroupColumn));
        ValidateColumn(mapping.RosterNumberColumn, document.Headers.Count, nameof(mapping.RosterNumberColumn));
        ValidateColumn(mapping.ClassOrCategoryColumn, document.Headers.Count, nameof(mapping.ClassOrCategoryColumn));

        var preliminary = document.Rows.Select(row =>
        {
            var firstName = GetValue(row, mapping.FirstNameColumn);
            var lastName = GetValue(row, mapping.LastNameColumn);
            var normalizedName = NormalizeName(firstName, lastName);
            var groupParts = new[]
            {
                GetValue(row, mapping.TeamOrSchoolColumn),
                GetValue(row, mapping.SportOrGroupColumn)
            }.Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value!.Trim());

            return new PreviewCandidate(
                row,
                firstName,
                lastName,
                normalizedName,
                string.Join(" / ", groupParts),
                GetValue(row, mapping.RosterNumberColumn),
                GetValue(row, mapping.ClassOrCategoryColumn));
        }).ToArray();

        var possibleMatches = preliminary
            .Where(candidate => candidate.NormalizedName.Length > 0)
            .GroupBy(candidate => candidate.NormalizedName, StringComparer.Ordinal)
            .Where(group => group.Select(candidate => candidate.GroupName)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Skip(1)
                .Any())
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);

        return preliminary.Select(candidate => new RosterPreviewRow(
            candidate.Row.RecordNumber,
            candidate.FirstName,
            candidate.LastName,
            string.Join(" ", new[] { candidate.FirstName, candidate.LastName }
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value!.Trim())),
            string.IsNullOrWhiteSpace(candidate.GroupName) ? null : candidate.GroupName,
            candidate.RosterNumber,
            candidate.ClassOrCategory,
            possibleMatches.GetValueOrDefault(candidate.NormalizedName),
            candidate.Row.HasExpectedFieldCount,
            candidate.Row.ToSourceDataJson())).ToArray();
    }

    private static string? GetValue(RosterCsvRow row, int? columnIndex) =>
        columnIndex is int index && index < row.Cells.Count ? row.Cells[index].Value : null;

    private static string NormalizeName(string? firstName, string? lastName)
    {
        var value = string.Join(" ", new[] { firstName, lastName }
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name!.Trim()));
        return value.Normalize(NormalizationForm.FormKC).ToUpperInvariant();
    }

    private static void ValidateColumn(int? columnIndex, int headerCount, string parameterName)
    {
        if (columnIndex is < 0 || columnIndex >= headerCount)
            throw new ArgumentOutOfRangeException(parameterName, columnIndex, "Mapped column must refer to a header in the roster file.");
    }

    private sealed record PreviewCandidate(
        RosterCsvRow Row,
        string? FirstName,
        string? LastName,
        string NormalizedName,
        string GroupName,
        string? RosterNumber,
        string? ClassOrCategory);
}
