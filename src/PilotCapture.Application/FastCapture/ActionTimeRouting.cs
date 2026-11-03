using PilotCapture.Domain;

namespace PilotCapture.Application.FastCapture;

public sealed record ActionTimeMatch(string? TeamId, long? UtcTicks, string? SampleId, string Issue);

public static class ActionTimeRouting
{
    public static ActionTimeMatch Resolve(long cameraTicks, IReadOnlyList<ActionClockSample> samples, IReadOnlyList<ActionChangeover> changes)
    {
        var ordered = changes.OrderBy(c => c.UtcTicks).ThenBy(c => c.Id).ToArray();
        var matches = new List<(ActionClockSample Clock, long Utc, string Team)>();
        foreach (var sample in samples)
        {
            long utc;
            try { utc = checked(cameraTicks + sample.OffsetTicks); }
            catch (OverflowException) { continue; }
            if (utc < sample.FromUtcTicks || utc >= sample.UntilUtcTicks) continue;
            var low = utc - sample.UncertaintyTicks; var high = utc + sample.UncertaintyTicks;
            var before = ordered.LastOrDefault(c => c.UtcTicks <= low);
            var after = ordered.LastOrDefault(c => c.UtcTicks <= high);
            if (before is null || after is null) return new(null, utc, sample.Id, "Before first recorded team changeover");
            if (before.TeamId != after.TeamId) return new(null, utc, sample.Id, "Capture time is too close to a team changeover");
            matches.Add((sample, utc, before.TeamId));
        }
        if (matches.Count == 0) return new(null, null, null, "Camera clock not verified for this capture time");
        if (matches.Select(m => m.Team).Distinct().Count() != 1) return new(null, null, null, "Conflicting camera clock history; select the correct performance");
        var match = matches.OrderByDescending(m => m.Clock.FromUtcTicks).First();
        return new(match.Team, match.Utc, match.Clock.Id, "");
    }
}
