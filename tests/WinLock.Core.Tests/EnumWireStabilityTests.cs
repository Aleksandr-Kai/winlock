using WinLock.Core.Models;

namespace WinLock.Core.Tests;

/// <summary>LockReason and NoticeKind are both sent over the wire as plain integers (System.Text.Json's
/// default enum encoding) and are hand-mirrored on the Android side (model/LockReason.kt,
/// model/NoticeKind.kt) since there's no shared codegen between the two languages. If either
/// assertion below needs to change, the matching Kotlin enum almost certainly needs the same
/// change — see EnumWireStabilityTest.kt on the Android side. These exist so a divergence is a
/// build-time test failure instead of a runtime mismatch only ever caught by an actual paired
/// phone and PC.</summary>
public class EnumWireStabilityTests
{
    [Fact]
    public void LockReason_MatchesTheKotlinMirror()
    {
        Assert.Equal(
            ["None", "OutsideAllowedWindow", "BudgetExhausted", "ClockTamperSuspected", "ManuallyLocked"],
            Enum.GetNames<LockReason>());
    }

    [Fact]
    public void NoticeKind_MatchesTheKotlinMirror()
    {
        Assert.Equal(["StateRecovery", "ServiceStopped"], Enum.GetNames<NoticeKind>());
    }
}
