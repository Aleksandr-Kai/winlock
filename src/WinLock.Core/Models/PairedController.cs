namespace WinLock.Core.Models;

/// <summary>One paired parent app instance ("controller"). Each has its own secret, so
/// revoking one parent's phone never affects any other parent paired to the same PC.</summary>
public sealed class PairedController
{
    public Guid ControllerId { get; set; } = Guid.NewGuid();

    /// <summary>What the parent named their phone during pairing, e.g. "Мамин телефон".</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>32-byte secret shared with this controller. Established once, over the
    /// pairing QR — never transmitted over the network, before or after pairing.</summary>
    public byte[] SharedSecret { get; set; } = Array.Empty<byte>();

    public DateTimeOffset PairedAtUtc { get; set; }
}
