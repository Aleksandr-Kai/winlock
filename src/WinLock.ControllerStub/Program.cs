using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using WinLock.ControllerStub;
using WinLock.Core.Network;
using WinLock.Core.Pairing;

Console.OutputEncoding = Encoding.UTF8;
Console.WriteLine("=== WinLock Controller Stub — заглушка вместо Android, для проверки обмена ===");

var config = StubConfig.LoadOrCreateDefault();
Console.WriteLine($"Конфиг: {Path.Combine(AppContext.BaseDirectory, "stub-config.json")}");
Console.WriteLine($"Адрес ПК (для запуска pairing): {config.ServerUrl}");
Console.WriteLine();

// Bootstrapping only: /agent/pair/begin stands in for scanning a QR that was displayed by a
// physically-present admin (see the Setup tool). A real controller never talks to a server
// it hasn't already pinned — this one call is the one deliberate exception, and it's
// loopback-only on the server side for exactly that reason, which also means it only works
// when this stub runs on the very same machine as the PC. Anywhere else — a separate host,
// a VM this one can't route to — paste the QR text shown by the Setup tool instead.
using var bootstrapHandler = new HttpClientHandler
{
    ServerCertificateCustomValidationCallback = (_, _, _, _) => true,
};
using var bootstrapClient = new HttpClient(bootstrapHandler);

string? qrText = null;
Console.WriteLine("Запрашиваем QR у службы (эмулируем 'Начать привязку' в Setup-инструменте)...");
try
{
    var httpResponse = await bootstrapClient.PostAsync($"{config.ServerUrl}/agent/pair/begin", content: null);
    if (httpResponse.IsSuccessStatusCode)
    {
        var beginResponse = await httpResponse.Content.ReadFromJsonAsync<BeginPairingResponse>();
        qrText = beginResponse?.QrText;
    }
    else
    {
        Console.WriteLine($"Сервер ответил {(int)httpResponse.StatusCode} — вероятно, ПК и заглушка не на одной машине.");
    }
}
catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
{
    Console.WriteLine($"Не удалось подключиться к {config.ServerUrl}: {ex.Message}");
}

if (qrText is null)
{
    Console.WriteLine();
    Console.WriteLine("Запустите на ПК ярлык «WinLock — Настройка» и вставьте сюда текст под QR-кодом");
    Console.WriteLine("(поле с текстом, а не саму картинку):");
    Console.Write("> ");
    qrText = Console.ReadLine();
}

if (qrText is null || !PairingQrPayload.TryParse(qrText, out var qr) || qr is null)
{
    Console.WriteLine("Не удалось разобрать QR-код.");
    return;
}

Console.WriteLine($"QR получен: устройство «{qr.DeviceDisplayName}» ({qr.DeviceId}), адрес {qr.HostAndPort}");
Console.WriteLine($"Отпечаток сертификата (pinned): {qr.CertificateFingerprintHex}");

// From here on, every connection is pinned to the exact certificate fingerprint from the
// QR — this is what a real phone does after actually scanning it.
using var pinnedHandler = new HttpClientHandler
{
    ServerCertificateCustomValidationCallback = (_, cert, _, _) => CertificatePinning.Validate(qr.CertificateFingerprintHex, cert),
};
using var pinnedClient = new HttpClient(pinnedHandler);
var baseUrl = $"https://{qr.HostAndPort}";

Console.WriteLine();
Console.WriteLine("Завершаем pairing (эмулируем сканирование QR телефоном)...");
var completeResponse = await pinnedClient.PostAsJsonAsync($"{baseUrl}/agent/pair",
    new PairCompletionRequest(qr.Token, config.ControllerDisplayName));
var completion = await completeResponse.Content.ReadFromJsonAsync<PairCompletionResponse>();

if (completion is null || !completion.Success || completion.ControllerId is not { } controllerId)
{
    Console.WriteLine("Pairing не удался.");
    return;
}

Console.WriteLine($"Привязано! ControllerId = {controllerId}");
Console.WriteLine();

using var socket = new ClientWebSocket();
socket.Options.RemoteCertificateValidationCallback = (_, cert, _, _) => CertificatePinning.Validate(qr.CertificateFingerprintHex, cert);
var wsUri = new Uri($"wss://{qr.HostAndPort}/agent/ws");

Console.WriteLine($"Открываем WebSocket {wsUri} ...");
await socket.ConnectAsync(wsUri, CancellationToken.None);

var authChallenge = await ReceiveAsync<ServerToControllerMessage>(socket);
if (authChallenge is not AuthChallenge challenge)
{
    Console.WriteLine("Служба не прислала AuthChallenge первым сообщением — протокол разошёлся.");
    return;
}

var responseBase64 = ControllerAuthenticator.ComputeAuthResponse(qr.Secret, challenge.Nonce);
await SendAsync(socket, new AuthResponse(controllerId, challenge.Nonce, responseBase64));

var authResult = await ReceiveAsync<ServerToControllerMessage>(socket);
if (authResult is not AuthResult { Success: true })
{
    Console.WriteLine("Аутентификация по WebSocket не прошла.");
    return;
}

Console.WriteLine("Аутентификация успешна. Слушаем статусы и жду команд.");
Console.WriteLine();
PrintHelp();

var receiveLoop = Task.Run(async () =>
{
    while (socket.State == WebSocketState.Open)
    {
        var message = await ReceiveAsync<ServerToControllerMessage>(socket);
        if (message is null)
        {
            Console.WriteLine("[соединение закрыто службой]");
            return;
        }

        PrintIncoming(message);
    }
});

while (true)
{
    Console.Write("> ");
    var line = Console.ReadLine();
    if (line is null || line.Trim() is "quit" or "exit")
        break;

    var parts = line.Trim().Split(' ', 2);
    var requestId = Guid.NewGuid().ToString("N")[..8];

    switch (parts[0])
    {
        case "extend" when parts.Length > 1 && int.TryParse(parts[1], out var minutes):
            await SendAsync(socket, new ExtendTimeCommand(requestId, minutes));
            break;

        case "screenshot":
            await SendAsync(socket, new RequestScreenshotCommand(requestId));
            break;

        case "help":
            PrintHelp();
            break;

        default:
            Console.WriteLine("Неизвестная команда. 'help' — список команд.");
            break;
    }
}

// CloseOutputAsync (not CloseAsync): the background receive loop still has a read pending
// on this same socket, and .NET WebSockets don't like a concurrent Close racing a Receive.
// This just sends our close frame and returns immediately; the receive loop observes the
// server's echoed close frame on its own and exits.
await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None);
await Task.WhenAny(receiveLoop, Task.Delay(TimeSpan.FromSeconds(2)));
return;

static void PrintHelp()
{
    Console.WriteLine("Команды: extend <минуты> | screenshot | help | quit");
}

static void PrintIncoming(ServerToControllerMessage message)
{
    switch (message)
    {
        case StatusUpdate status:
            Console.WriteLine($"[status] locked={status.IsLocked} reason={status.Reason} remaining={status.RemainingBudget:hh\\:mm\\:ss}");
            break;

        case CommandAck ack:
            Console.WriteLine($"[ack] {ack.RequestId} success={ack.Success} {ack.ErrorMessage}");
            break;

        case ScreenshotResult shot when shot.Success:
            var bytes = Convert.FromBase64String(shot.ImageBase64!);
            var path = Path.Combine(AppContext.BaseDirectory, $"screenshot-{shot.RequestId}.jpg");
            File.WriteAllBytes(path, bytes);
            Console.WriteLine($"[screenshot] сохранён: {path} ({bytes.Length} байт)");
            break;

        case ScreenshotResult shot:
            Console.WriteLine($"[screenshot] ошибка: {shot.ErrorMessage}");
            break;

        default:
            Console.WriteLine($"[?] {message}");
            break;
    }
}

static async Task SendAsync(ClientWebSocket socket, ControllerToServerMessage message)
{
    var json = JsonSerializer.SerializeToUtf8Bytes(message);
    await socket.SendAsync(json, WebSocketMessageType.Text, endOfMessage: true, CancellationToken.None);
}

static async Task<T?> ReceiveAsync<T>(ClientWebSocket socket) where T : class
{
    using var buffer = new MemoryStream();
    var chunk = new byte[8192];
    WebSocketReceiveResult result;
    do
    {
        result = await socket.ReceiveAsync(chunk, CancellationToken.None);
        if (result.MessageType == WebSocketMessageType.Close)
        {
            if (socket.State == WebSocketState.CloseReceived)
                await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None);
            return null;
        }

        buffer.Write(chunk, 0, result.Count);
    } while (!result.EndOfMessage);

    buffer.Position = 0;
    return await JsonSerializer.DeserializeAsync<T>(buffer);
}
