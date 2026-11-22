using PilotCapture.Domain;

namespace PilotCapture.Application.FastCapture;

public static class FastPerformanceSearch
{
    public static string Label(FastCaptureTeam team) =>
        $"{(team.PerformanceLabel.Length > 0 ? team.PerformanceLabel : team.SortOrder.ToString("000"))} · {team.Name}"
        + (team.Details.Length > 0 ? $" · {DetailValues(team.Details)}" : "");

    // Stored details retain their source headers. Capture choices show values only,
    // including for jobs imported by earlier previews.
    public static string DetailValues(string details) => string.Join(" · ", details.Split(" · ").Select(part =>
        part.IndexOf(": ", StringComparison.Ordinal) is var colon && colon >= 0 ? part[(colon + 2)..] : part));

    public static IReadOnlyList<FastCaptureTeam> Schedule(IReadOnlyList<FastCaptureTeam> teams, string day, string hall) =>
        teams.Where(t => t.Day.Equals(day, StringComparison.OrdinalIgnoreCase) && t.Hall.Equals(hall, StringComparison.OrdinalIgnoreCase)).ToArray();

    public static IReadOnlyList<FastCaptureTeam> Find(IReadOnlyList<FastCaptureTeam> teams, string? query)
    {
        var words = (query ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return teams.Where(t => words.All(word => Label(t).Contains(word, StringComparison.OrdinalIgnoreCase))).ToArray();
    }
}
