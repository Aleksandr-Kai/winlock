package com.winlock.parent.protocol

/** This app's own version, and the oldest PC-agent version (see the PC's own AgentVersion)
 * it's known to interoperate with — shown together at the bottom of the main screen so a
 * parent can tell, without opening any device, whether the phone itself is what needs
 * updating. [MinCompatibleAgentVersion] is bumped by hand alongside a wire-protocol change
 * that actually requires it — most releases don't move it at all. */
object AppVersion {
    const val Current = "1.0.0"
    const val MinCompatibleAgentVersion = "1.0.0"
}
