using System.Runtime.Versioning;
using System.Security.Cryptography;
using WinLock.Core.State;

namespace WinLock.Service.Security;

/// <summary>
/// Encrypts the state file with Windows DPAPI at machine scope: only code running on this
/// same machine (as SYSTEM or an administrator) can decrypt it, so a child copying the file
/// out or hand-editing it sees only ciphertext.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class DpapiStateProtector : IStateProtector
{
    // Ties the ciphertext to this specific purpose so it can't be swapped for a blob
    // protected for a different feature that happens to also run as SYSTEM on this machine.
    private static readonly byte[] Entropy = "WinLock.AgentState.v1"u8.ToArray();

    public byte[] Protect(byte[] plaintext) =>
        ProtectedData.Protect(plaintext, Entropy, DataProtectionScope.LocalMachine);

    public byte[] Unprotect(byte[] protectedData) =>
        ProtectedData.Unprotect(protectedData, Entropy, DataProtectionScope.LocalMachine);
}
