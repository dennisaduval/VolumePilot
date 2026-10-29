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
}
