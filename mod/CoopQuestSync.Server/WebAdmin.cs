using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using ReadyM.Api.Idents;
using ReadyM.SDK.Attributes;

namespace CoopQuestSync.Server;

/// <summary>
/// A small admin web page on its own port (OblivionMP's own admin panel on 9050 has no way for mods to add pages).
/// It lists every quest with each player's progress, and lets an admin share a stage, reset a quest, pick whose
/// progress is shared, and turn auto sync on or off.
///
/// It is a deliberately tiny HTTP server on a raw socket, so it needs no admin rights or URL reservation on Windows.
/// Requests run on a background thread; anything that changes the game goes through <see cref="QuestTokenSync.Enqueue"/>.
/// </summary>
[Service]
public sealed partial class WebAdmin(QuestTokenSync sync, GameData data, QuestSyncConfig config, CombatService combat, ILogger logger)
{
    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private byte[] _page = [];

    private void Start()
    {
        if (config.WebPort <= 0)
        {
            logger.LogInformation("[QuestSync] Web page is off (WebPort is 0).");
            return;
        }
        if (!IPAddress.TryParse(config.WebBind, out var address))
        {
            logger.LogWarning("[QuestSync] Web page is off: WebBind '{Bind}' is not an IP address.", config.WebBind);
            return;
        }
        if (!IPAddress.IsLoopback(address) && string.IsNullOrEmpty(config.WebPassword))
        {
            logger.LogWarning("[QuestSync] Web page is off: it would be open to other computers ({Bind}) without a WebPassword. Set one in config.json.", config.WebBind);
            return;
        }
        using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("CoopQuestSync.web.index.html"))
        {
            using var ms = new MemoryStream();
            stream!.CopyTo(ms);
            _page = ms.ToArray();
        }
        try
        {
            _listener = new TcpListener(address, config.WebPort);
            _listener.Start();
        }
        catch (Exception e)
        {
            logger.LogWarning("[QuestSync] Web page could not start on {Bind}:{Port}: {Error}", config.WebBind, config.WebPort, e.Message);
            return;
        }
        _cts = new CancellationTokenSource();
        _ = AcceptLoop(_listener, _cts.Token);
        var shown = IPAddress.IsLoopback(address) || address.Equals(IPAddress.Any) ? "localhost" : config.WebBind;
        logger.LogInformation("[QuestSync] Quest admin page: http://{Host}:{Port}/", shown, config.WebPort);
    }

    private void Stop()
    {
        _cts?.Cancel();
        _listener?.Stop();
    }

    private async Task AcceptLoop(TcpListener listener, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            TcpClient client;
            try { client = await listener.AcceptTcpClientAsync(ct); }
            catch { return; }
            _ = Task.Run(() => Handle(client), ct);
        }
    }

    private async Task Handle(TcpClient client)
    {
        using var _ = client;
        client.ReceiveTimeout = 5000;
        client.SendTimeout = 5000;
        try
        {
            var stream = client.GetStream();
            var request = await ReadRequest(stream);
            if (request is null)
                return;
            var (status, type, body) = Route(request);
            var head = $"HTTP/1.1 {status}\r\nContent-Type: {type}\r\nContent-Length: {body.Length}\r\n" +
                       "Cache-Control: no-store\r\nX-Content-Type-Options: nosniff\r\nConnection: close\r\n\r\n";
            await stream.WriteAsync(Encoding.ASCII.GetBytes(head));
            await stream.WriteAsync(body);
        }
        catch (Exception e)
        {
            logger.LogDebug("[QuestSync] Web request failed: {Error}", e.Message);
        }
    }

    private sealed record Request(string Method, string Path, Dictionary<string, string> Headers, string Body);

    private static async Task<Request?> ReadRequest(NetworkStream stream)
    {
        var buffer = new byte[64 * 1024];
        var length = 0;
        int headerEnd;
        while ((headerEnd = IndexOf(buffer, length, "\r\n\r\n"u8)) < 0)
        {
            if (length == buffer.Length)
                return null;
            var read = await stream.ReadAsync(buffer.AsMemory(length));
            if (read == 0)
                return null;
            length += read;
        }
        var headerText = Encoding.ASCII.GetString(buffer, 0, headerEnd);
        var lines = headerText.Split("\r\n");
        var first = lines[0].Split(' ');
        if (first.Length < 2)
            return null;
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in lines.Skip(1))
        {
            var colon = line.IndexOf(':');
            if (colon > 0)
                headers[line[..colon].Trim()] = line[(colon + 1)..].Trim();
        }
        var bodyLength = headers.TryGetValue("Content-Length", out var cl) && int.TryParse(cl, out var n) ? n : 0;
        if (bodyLength < 0 || bodyLength > buffer.Length - headerEnd - 4)
            return null;
        var bodyStart = headerEnd + 4;
        while (length < bodyStart + bodyLength)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(length));
            if (read == 0)
                return null;
            length += read;
        }
        var body = Encoding.UTF8.GetString(buffer, bodyStart, bodyLength);
        return new Request(first[0], first[1].Split('?')[0], headers, body);
    }

    private static int IndexOf(byte[] data, int length, ReadOnlySpan<byte> needle)
        => data.AsSpan(0, length).IndexOf(needle);

    private (string Status, string Type, byte[] Body) Route(Request r)
    {
        if (r.Method == "GET" && (r.Path == "/" || r.Path == "/index.html"))
            return ("200 OK", "text/html; charset=utf-8", _page);

        if (!string.IsNullOrEmpty(config.WebPassword) &&
            (!r.Headers.TryGetValue("X-Admin-Password", out var given) || !FixedTimeEquals(given, config.WebPassword)))
            return Json("401 Unauthorized", new { error = "password" });

        if (r.Method == "GET" && r.Path == "/api/map")
            return ("200 OK", "application/json", Encoding.UTF8.GetBytes(data.WebMapJson()));
        if (r.Method == "GET" && r.Path == "/api/state")
            return ("200 OK", "application/json", Encoding.UTF8.GetBytes(sync.StateJson));
        if (r.Method == "POST" && r.Path == "/api/action")
        {
            // Browsers send this header only from our own page's script, which keeps other sites from posting here.
            if (!r.Headers.ContainsKey("X-Requested-With"))
                return Json("403 Forbidden", new { error = "missing header" });
            return Act(r.Body);
        }
        return Json("404 Not Found", new { error = "not found" });
    }

    private (string, string, byte[]) Act(string body)
    {
        JsonElement a;
        try { a = JsonDocument.Parse(body).RootElement; }
        catch { return Json("400 Bad Request", new { error = "bad json" }); }
        var action = a.TryGetProperty("action", out var act) ? act.GetString() : null;
        string? Str(string name) => a.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

        switch (action)
        {
            case "auto":
                var on = a.TryGetProperty("on", out var o) && o.ValueKind == JsonValueKind.True;
                sync.Enqueue(() => sync.SetAuto(on));
                break;

            case "source":
                var source = Str("name");
                sync.Enqueue(() => sync.SetSource(source));
                break;

            case "push":
            {
                var quest = Str("quest");
                if (quest is null || !sync.Map.TryNormalizeQuest(quest, out var name) || !a.TryGetProperty("stage", out var st) || !st.TryGetInt32(out var stage)
                    || !sync.Map.TryGetToken(new QuestStage(name, stage), out _))
                    return Json("400 Bad Request", new { error = "unknown quest or stage" });
                if (!sync.Map.IsShared(name))
                    return Json("400 Bad Request", new { error = "this quest is never shared" });
                var player = Str("player");
                sync.Enqueue(() =>
                {
                    PlayerId? only = null;
                    if (!string.IsNullOrEmpty(player))
                    {
                        foreach (var (id, _) in sync.Players)
                            if (sync.Nickname(id) == player)
                                only = id;
                        if (only is null)
                            return;
                    }
                    var count = sync.AdminPush(new QuestStage(name, stage), only);
                    logger.LogInformation("[QuestSync] Web: pushed {Quest} {Stage} to {Count} player(s).", name, stage, count);
                });
                break;
            }

            case "reset-quest":
            {
                var quest = Str("quest");
                if (quest is null || !sync.Map.TryNormalizeQuest(quest, out var name))
                    return Json("400 Bad Request", new { error = "unknown quest" });
                sync.Enqueue(() => sync.ResetQuest(name));
                break;
            }

            case "set":
            {
                var key = Str("key");
                if (key is null || !a.TryGetProperty("value", out var value))
                    return Json("400 Bad Request", new { error = "key and value needed" });
                var copy = value.Clone();
                sync.Enqueue(() =>
                {
                    var error = sync.ChangeSetting(key, copy);
                    if (error is not null)
                        logger.LogWarning("[QuestSync] Web: setting {Key} refused: {Error}", key, error);
                });
                break;
            }

            case "calm":
            {
                // A player's name (their area and bounty), or "all" for every area with a player in it.
                var who = Str("player");
                sync.Enqueue(() => logger.LogInformation("[QuestSync] Web: {Result}", combat.Calm(null, string.IsNullOrEmpty(who) ? "all" : who)));
                break;
            }

            case "reset-all":
                sync.Enqueue(sync.ClearShared);
                break;

            default:
                return Json("400 Bad Request", new { error = "unknown action" });
        }
        return Json("202 Accepted", new { ok = true });
    }

    private static (string, string, byte[]) Json(string status, object value)
        => (status, "application/json", JsonSerializer.SerializeToUtf8Bytes(value));

    private static bool FixedTimeEquals(string a, string b)
        => System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(a), Encoding.UTF8.GetBytes(b));
}
