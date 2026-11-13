using PilotCapture.Application.FastCapture;
using PilotCapture.Domain;
using Xunit;

namespace PilotCapture.Application.Tests;

public sealed class ActionTimeRoutingTests
{
    [Fact]
    public void Corrects_wrong_dates_and_holds_uncertain_changeovers_or_conflicting_epochs()
    {
        var start = new DateTime(2026, 10, 29, 9, 0, 0, DateTimeKind.Utc).Ticks;
        var shift = TimeSpan.FromDays(400).Ticks;
        ActionChangeover[] changes = [new() { TeamId = "A", UtcTicks = start }, new() { TeamId = "B", UtcTicks = start + TimeSpan.FromMinutes(3).Ticks }];
        ActionClockSample[] samples = [new() { Id = "clock", FromUtcTicks = start, UntilUtcTicks = start + TimeSpan.FromHours(2).Ticks, OffsetTicks = shift, UncertaintyTicks = TimeSpan.FromSeconds(1).Ticks }];
        var a = ActionTimeRouting.Resolve(start + TimeSpan.FromSeconds(150).Ticks - shift, samples, changes);
        Assert.Equal("A", a.TeamId); Assert.Equal("clock", a.SampleId);
        var b = ActionTimeRouting.Resolve(start + TimeSpan.FromSeconds(200).Ticks - shift, samples, changes);
        Assert.Equal("B", b.TeamId);
        Assert.Null(ActionTimeRouting.Resolve(start + TimeSpan.FromMinutes(3).Ticks - shift, samples, changes).TeamId);
        Assert.Null(ActionTimeRouting.Resolve(start - shift, [], changes).TeamId);
        var conflict = samples.Concat([new ActionClockSample { FromUtcTicks = start, UntilUtcTicks = start + TimeSpan.FromHours(2).Ticks, OffsetTicks = shift + TimeSpan.FromMinutes(4).Ticks }]).ToArray();
        Assert.Null(ActionTimeRouting.Resolve(start + TimeSpan.FromSeconds(30).Ticks - shift, conflict, changes).TeamId);
    }

    [Theory]
    [InlineData(0, 3)]
    [InlineData(500, 3)]
    [InlineData(1000, 2)]
    [InlineData(2999, 1)]
    [InlineData(3000, 0)]
    public void Cue_counts_down_from_the_effective_changeover(int elapsedMs, int expected)
    {
        var start = new DateTime(2026, 11, 13, 12, 0, 0, DateTimeKind.Utc).Ticks;
        Assert.Equal(expected, ActionTimeRouting.ChangeoverCueRemainingSeconds(start, start + TimeSpan.FromMilliseconds(elapsedMs).Ticks));
    }

    [Fact]
    public void Uses_a_short_shooting_gap_and_holds_a_brief_switch_away_and_back()
    {
        var start = new DateTime(2026, 11, 13, 12, 0, 0, DateTimeKind.Utc);
        long At(double seconds) => start.AddSeconds(seconds).Ticks;
        ActionClockSample[] samples = [new() { FromUtcTicks = At(0), UntilUtcTicks = At(100), UncertaintyTicks = TimeSpan.FromSeconds(2).Ticks }];
        ActionChangeover[] changes = [new() { TeamId = "A", UtcTicks = At(0) }, new() { TeamId = "B", UtcTicks = At(40) }];
        Assert.Equal("A", ActionTimeRouting.Resolve(At(37), samples, changes).TeamId);
        Assert.Equal("B", ActionTimeRouting.Resolve(At(43), samples, changes).TeamId);
        Assert.Null(ActionTimeRouting.Resolve(At(39), samples, changes).TeamId);
        var returned = changes.Append(new ActionChangeover { TeamId = "A", UtcTicks = At(41) }).ToArray();
        Assert.Null(ActionTimeRouting.Resolve(At(40), samples, returned).TeamId);
    }
}
