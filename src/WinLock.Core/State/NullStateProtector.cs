namespace WinLock.Core.State;

/// <summary>Pass-through protector for tests and non-Windows builds. Never use in production.</summary>
public sealed class NullStateProtector : IStateProtector
{
    public byte[] Protect(byte[] plaintext) => plaintext;

    public byte[] Unprotect(byte[] protectedData) => protectedData;
}
