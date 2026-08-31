#!/usr/bin/env bash
# Publishes WinLock.Service and WinLock.Agent.UI as framework-dependent win-x64 builds into
# installer/payload/ (what actually gets installed on the child's PC), WinLock.Setup as a
# self-contained single-file win-x64 exe directly into installer/ (the double-click GUI
# installer — self-contained so it can run, and warn about a missing runtime, even on a PC
# that has none of .NET installed yet), and the controller stub as a self-contained
# single-file win-x64 exe into dist/ (a separate testing tool, not part of the product — run
# it on whatever machine plays the "parent's phone" role while there's no real Android app
# yet), and WinLock.Diagnose the same way into dist/Diagnose/ (a standalone read-only
# diagnostic tool — run it directly on the child's PC when something's wrong and it can't be
# figured out remotely; it writes a text report next to itself, so running it straight off the
# USB drive is exactly the point). Run this after any code change, before re-running
# WinLock-Install.exe. Can be run from Linux or Windows — dotnet's cross-publish for win-x64
# doesn't need a Windows host either way.
set -euo pipefail
cd "$(dirname "$0")/.."

rm -rf installer/payload installer/WinLock-Install.exe dist/ControllerStub-Windows dist/Diagnose
dotnet publish src/WinLock.Service -c Release -r win-x64 --self-contained false -o installer/payload/Service
dotnet publish src/WinLock.Agent.UI -c Release -r win-x64 --self-contained false -o installer/payload/UI
dotnet publish src/WinLock.Setup -c Release -r win-x64 --self-contained true \
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=none \
    -o installer/
dotnet publish src/WinLock.ControllerStub -c Release -r win-x64 --self-contained true \
    -p:PublishSingleFile=true -o dist/ControllerStub-Windows
dotnet publish src/WinLock.Diagnose -c Release -r win-x64 --self-contained true \
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=none \
    -o dist/Diagnose

echo
echo "Installer ready in installer/ — copy the whole 'installer' folder to the child's"
echo "Windows machine and double-click WinLock-Install.exe (it will prompt for admin rights"
echo "itself; no PowerShell or console needed)."
echo
echo "Controller stub ready in dist/ControllerStub-Windows/ — copy WinLock.ControllerStub.exe"
echo "to whatever machine will act as the parent's phone and run it there."
echo
echo "Diagnostic tool ready in dist/Diagnose/ — copy WinLock-Diagnose.exe to the child's PC"
echo "and run it there; it writes a WinLock-Diagnostics-<timestamp>.txt report next to itself."
