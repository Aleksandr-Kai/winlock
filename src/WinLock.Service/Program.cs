using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using WinLock.Core;
using WinLock.Core.Locking;
using WinLock.Core.Network;
using WinLock.Core.State;
using WinLock.Core.Timing;
using WinLock.Core.Warnings;
using WinLock.Service;
using WinLock.Service.Ipc;
using WinLock.Service.Network;
using WinLock.Service.Notifications;
using WinLock.Service.Screenshots;
using WinLock.Service.Security;

const int NetworkPort = 51843;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddWindowsService(options => options.ServiceName = "WinLock Agent");

// Overridable so this can run and be tested (e.g. against the controller stub) without
// permission to write %ProgramData% — on the real Windows deployment the service runs as
// SYSTEM, which always can, so this only ever matters for local dev/test.
var dataDir = Environment.GetEnvironmentVariable("WINLOCK_DATA_DIR")
    ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "WinLock");
var stateFilePath = Path.Combine(dataDir, "state.json");

// DPAPI is Windows-only; the state store still needs to build and run in tests/dev on
// other platforms, so fall back to a pass-through protector there.
IStateProtector protector = OperatingSystem.IsWindows()
    ? new DpapiStateProtector()
    : new NullStateProtector();

var stateStore = new JsonFileStateStore(stateFilePath, protector);

// Loaded once, synchronously, at startup: it's a small local file read, and the rest of
// the host's DI graph (the runtime, the worker) is built from its result.
var persisted = stateStore.LoadAsync().GetAwaiter().GetResult();

if (string.IsNullOrWhiteSpace(persisted.Pairing.DeviceDisplayName))
    persisted.Pairing.DeviceDisplayName = Environment.MachineName;

var tracker = new UsageTracker(new SystemMonotonicClock(), persisted.Schedule, persisted.Usage);
var runtime = new AgentRuntime(tracker, persisted.Schedule, persisted.Pairing, persisted.Offline);

builder.Services.AddSingleton(runtime);
builder.Services.AddSingleton<IStateStore>(stateStore);

// The network channel (pairing + WebSocket) is plain cross-platform code — only the lock
// screen and screenshot capture genuinely need Windows (a desktop session to draw into).
// Keeping it unguarded is what lets the whole exchange be exercised on any dev machine,
// against a stand-in controller, before ever touching a Windows box.
var (certificate, pfxBytes) = DeviceCertificateProvider.GetOrCreate(persisted.Pairing.CertificatePfx);
if (persisted.Pairing.CertificatePfx != pfxBytes)
{
    persisted.Pairing.CertificatePfx = pfxBytes;
    stateStore.SaveAsync(persisted).GetAwaiter().GetResult();
}
var certificateFingerprintHex = DeviceCertificateProvider.ComputeFingerprintHex(certificate);

builder.WebHost.ConfigureKestrel(options =>
{
    options.Listen(IPAddress.Any, NetworkPort, listenOptions => listenOptions.UseHttps(certificate));
});

builder.Services.AddSingleton(sp => new ScreenCaptureCoordinator(sp.GetRequiredService<ILogger<ScreenCaptureCoordinator>>(), dataDir));
builder.Services.AddSingleton<ControllerHub>();
builder.Services.AddSingleton<IAgentStatusPublisher>(sp => sp.GetRequiredService<ControllerHub>());

if (OperatingSystem.IsWindows())
{
    // Guarded by the check above; the analyzer can't see through lambdas/local functions to
    // confirm that, so it flags a lot of this as reachable on all platforms.
#pragma warning disable CA1416
    builder.Services.AddSingleton<NamedPipeServerHost>();
    builder.Services.AddSingleton<ILockController>(sp => new IpcLockController(
        sp.GetRequiredService<NamedPipeServerHost>(),
        runtime,
        sp.GetRequiredService<ILogger<IpcLockController>>()));
    builder.Services.AddSingleton(sp => new PairingPipeHandler(
        sp.GetRequiredService<NamedPipeServerHost>(),
        runtime,
        certificateFingerprintHex,
        NetworkPort,
        sp.GetRequiredService<ILogger<PairingPipeHandler>>()));
    builder.Services.AddSingleton<ITimeWarningNotifier, TimeWarningNotifier>();
#pragma warning restore CA1416
}
else
{
    // The lock screen needs a real Windows desktop session; nothing stands in for it here.
    builder.Services.AddSingleton<ILockController, LoggingLockController>();
    builder.Services.AddSingleton<ITimeWarningNotifier, NullTimeWarningNotifier>();
}

builder.Services.AddHostedService<EnforcementWorker>();

var app = builder.Build();

if (OperatingSystem.IsWindows())
{
#pragma warning disable CA1416
    // Instantiate eagerly: it subscribes to the pipe host's events, and nothing else holds
    // a reference to it, so DI would otherwise never construct it at all.
    app.Services.GetRequiredService<PairingPipeHandler>();
#pragma warning restore CA1416
}

app.UseWebSockets();

// Loopback-only: starts pairing mode without going through the (Windows-only) Setup tool.
// On a real deployment that tool — launched elevated, so a non-admin child can't run it —
// is the intended way in; this exists so the exchange can be driven end to end from a plain
// console client while developing, and doubles as a future local web-based admin UI's entry
// point. Restricted to loopback because, unlike the pipe path, there's no OS-level identity
// check available over HTTP — reachability from this machine is the only guarantee we have.
app.MapPost("/agent/pair/begin", (HttpContext context) =>
{
    var remoteIp = context.Connection.RemoteIpAddress;
    if (remoteIp is null || !IPAddress.IsLoopback(remoteIp))
        return Results.StatusCode(StatusCodes.Status403Forbidden);

    var address = NetworkAddressHelper.GetPrimaryLocalIPv4() ?? "127.0.0.1";
    var qr = runtime.BeginPairing(TimeSpan.FromMinutes(5), certificateFingerprintHex, $"{address}:{NetworkPort}");
    return Results.Json(new BeginPairingResponse(qr.ToQrText(), DateTimeOffset.UtcNow.AddMinutes(5)));
});

app.MapPost("/agent/pair", (PairCompletionRequest request) =>
{
    var success = runtime.TryCompletePairing(request.Token, request.ControllerDisplayName, out var controllerId);

    if (success && OperatingSystem.IsWindows())
    {
#pragma warning disable CA1416
        app.Services.GetRequiredService<PairingPipeHandler>().NotifyPairingCompleted(request.ControllerDisplayName);
#pragma warning restore CA1416
    }

    return Results.Json(new PairCompletionResponse(success, success ? controllerId : null, runtime.DeviceId, runtime.DeviceDisplayName));
});

app.Map("/agent/ws", async (HttpContext context, ControllerHub hub) =>
{
    if (!context.WebSockets.IsWebSocketRequest)
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        return;
    }

    using var socket = await context.WebSockets.AcceptWebSocketAsync();
    await hub.HandleConnectionAsync(socket, context.RequestAborted);
});

app.Run();
