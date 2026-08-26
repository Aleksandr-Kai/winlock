namespace WinLock.Core.State;

/// <summary>
/// Encrypts/authenticates the persisted state blob so a child cannot hand-edit the JSON
/// file to grant themselves extra time. The Windows host wires in a DPAPI-backed (machine
/// scope) implementation; tests and non-Windows builds use a pass-through.
/// </summary>
public interface IStateProtector
{
    byte[] Protect(byte[] plaintext);
    byte[] Unprotect(byte[] protectedData);
}
