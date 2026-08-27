using System.Text.Json;
using WinLock.Core.Models;
using WinLock.Core.Network;

namespace WinLock.Core.Tests;

public class NetworkMessageSerializationTests
{
    [Fact]
    public void ServerToControllerMessages_RoundTrip_ThroughPolymorphicJson()
    {
        ServerToControllerMessage[] messages =
        [
            new AuthChallenge("abc"),
            new AuthResult(true),
            new StatusUpdate(Guid.NewGuid(), "Kid's PC", true, LockReason.BudgetExhausted, TimeSpan.FromMinutes(5)),
            new CommandAck("req-1", false, "nope"),
            new ScreenshotResult("req-2", true, null, "base64==", DateTimeOffset.UtcNow),
            new StateRecoveryWarning(DateTimeOffset.UtcNow, "IOException: disk hiccup"),
        ];

        foreach (var message in messages)
        {
            var json = JsonSerializer.Serialize(message);
            var roundTripped = JsonSerializer.Deserialize<ServerToControllerMessage>(json);
            Assert.Equal(message, roundTripped);
        }
    }

    [Fact]
    public void ControllerToServerMessages_RoundTrip_ThroughPolymorphicJson()
    {
        var schedule = new ScheduleConfig
        {
            DailyLimitMinutes = 90,
            AllowedWindows = new Dictionary<DayOfWeek, List<TimeWindow>>
            {
                [DayOfWeek.Monday] = [new TimeWindow(new TimeOnly(8, 0), new TimeOnly(20, 0))],
            },
        };

        ControllerToServerMessage[] messages =
        [
            new AuthResponse(Guid.NewGuid(), "nonce", "resp=="),
            new ExtendTimeCommand("req-1", 30),
            new SetRemainingTimeCommand("req-1b", 360),
            new UpdateScheduleCommand("req-2", schedule),
            new RequestScreenshotCommand("req-3"),
            new AcknowledgeStateRecoveryCommand("req-4"),
        ];

        foreach (var message in messages)
        {
            var json = JsonSerializer.Serialize(message);
            var roundTripped = JsonSerializer.Deserialize<ControllerToServerMessage>(json);
            Assert.IsType(message.GetType(), roundTripped);
        }

        var scheduleJson = JsonSerializer.Serialize(messages[3]);
        var scheduleRoundTripped = Assert.IsType<UpdateScheduleCommand>(
            JsonSerializer.Deserialize<ControllerToServerMessage>(scheduleJson));
        Assert.Equal(90, scheduleRoundTripped.Schedule.DailyLimitMinutes);
        Assert.True(scheduleRoundTripped.Schedule.IsWithinAllowedWindow(new DateTimeOffset(2026, 8, 24, 10, 0, 0, TimeSpan.Zero))); // a Monday
    }
}
