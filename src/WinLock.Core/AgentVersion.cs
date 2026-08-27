namespace WinLock.Core;

/// <summary>
/// Version of the PC-side agent — WinLock.Service and WinLock.Agent.UI are always built and
/// shipped together via installer/build-payload.sh, so one version number covers both rather
/// than tracking separate assembly versions that could drift apart. Bumped by hand on
/// releases that change the wire protocol or fix something a parent would want to know is
/// fixed. Sent to every paired phone on connect (see ServerToControllerMessage.AgentVersionInfo)
/// so a parent can tell, from the phone alone, whether a given PC needs updating.
/// </summary>
public static class AgentVersion
{
    public const string Current = "1.0.0";
}
