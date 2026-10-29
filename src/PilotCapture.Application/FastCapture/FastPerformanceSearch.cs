using PilotCapture.Domain;

namespace PilotCapture.Application.FastCapture;

public static class FastPerformanceSearch
{
    public static string Label(FastCaptureTeam team) =>
        $"{(team.PerformanceLabel.Length > 0 ? team.PerformanceLabel : team.SortOrder.ToString("000"))} · {team.Name}"
        + (team.Details.Length > 0 ? $" · {team.Details}" : "");

    public static IReadOnlyList<FastCaptureTeam> Find(IReadOnlyList<FastCaptureTeam> teams, string? query)
    {
        var words = (query ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return teams.Where(t => words.All(word => Label(t).Contains(word, StringComparison.OrdinalIgnoreCase))).ToArray();
    }
}
