using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Native.Protocol.Share;

public enum ShareKind { File, Text }

/// <summary>Algo que o PC deixou disponível para o iPhone buscar.</summary>
public sealed class ShareItem
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public long Size { get; init; }
    public ShareKind Kind { get; init; }
    public string? Path { get; init; }
    public string? Text { get; init; }
    public DateTime Created { get; init; }
    public bool Delivered { get; internal set; }
}

public sealed record ReceivedFile(string Path, string Name, long Size, string? ContentType);

/// <summary>O que está copiado: texto (às vezes RTF) ou imagem.</summary>
public sealed record ClipData(string? Text, byte[]? Image, string? ImageType = null, bool IsRtf = false)
{
    public bool IsEmpty => string.IsNullOrEmpty(Text) && (Image is null || Image.Length == 0);

    /// <summary>Interpreta o corpo enviado pelo iPhone (Atalho "Copiar para o PC").</summary>
    public static ClipData FromBody(byte[] body, string? contentType)
    {
        var type = (contentType ?? "").Split(';')[0].Trim().ToLowerInvariant();
        if (type.StartsWith("image/", StringComparison.Ordinal) || LooksLikeImage(body))
            return new ClipData(null, body, type.StartsWith("image/", StringComparison.Ordinal) ? type : SniffImage(body));
        var text = Encoding.UTF8.GetString(body).TrimStart('\uFEFF');
        bool rtf = type is "text/rtf" or "application/rtf" || text.StartsWith("{\\rtf", StringComparison.Ordinal);
        return new ClipData(rtf ? text : text.Trim(), null, null, rtf);
    }

    private static bool LooksLikeImage(byte[] b) => SniffImage(b) is not null;

    private static string? SniffImage(byte[] b)
    {
        if (b.Length > 8 && b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47) return "image/png";
        if (b.Length > 3 && b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF) return "image/jpeg";
        if (b.Length > 12 && b[4] == 0x66 && b[5] == 0x74 && b[6] == 0x79 && b[7] == 0x70) return "image/heic";
        if (b.Length > 6 && b[0] == 0x47 && b[1] == 0x49 && b[2] == 0x46) return "image/gif";
        return null;
    }
}

/// <summary>
/// Servidor HTTP do compartilhamento, só para a rede local e protegido por uma chave.
/// O iPhone usa pela página do Native (Safari / Tela de Início) ou pelo Atalho "Enviar para o PC".
/// </summary>
public sealed class ShareServer : IDisposable
{
    private readonly object _lock = new();
    private readonly List<ShareItem> _outbox = new();
    private readonly Func<string> _receiveFolder;
    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private long _lastSeenTicks;

    public string Token { get; set; }
    public string DisplayName { get; set; } = "PC";

    /// <summary>Endereço base que o Atalho do iPhone usa (ex.: http://native-pc.local:47811).</summary>
    public string? ShortcutBase { get; set; }
    public byte[]? TouchIcon { get; set; }
    public int Port { get; private set; }
    public bool IsRunning => _listener is not null;

    public event Action<ReceivedFile>? FileReceived;
    public event Action<string>? TextReceived;
    public event Action<ClipData>? ClipboardReceived;

    /// <summary>Lê a área de transferência do PC (na thread da interface). Nulo: o usuário desligou o recurso.</summary>
    public Func<Task<ClipData?>>? ClipboardProvider { get; set; }
    public event Action<ShareItem>? ItemDelivered;
    public event Action? OutboxChanged;
    public event Action? PhoneSeen;

    public DateTime LastPhoneSeen => new(Interlocked.Read(ref _lastSeenTicks));

    public ShareServer(string token, Func<string> receiveFolder)
    {
        Token = token;
        _receiveFolder = receiveFolder;
    }

    public static string NewToken() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(16)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    // ------------------------------------------------------------ ciclo de vida

    /// <summary>Abre a porta (tenta a preferida e as 9 seguintes).</summary>
    public void Start(int preferredPort, IPAddress? bind = null)
    {
        if (_listener is not null) return;
        SocketException? last = null;
        for (int p = preferredPort; p < preferredPort + 10; p++)
        {
            var l = new TcpListener(bind ?? IPAddress.Any, p);
            try
            {
                l.Start();
                _listener = l;
                Port = ((IPEndPoint)l.LocalEndpoint).Port;
                break;
            }
            catch (SocketException ex) { last = ex; }
        }
        if (_listener is null) throw last ?? new SocketException((int)SocketError.AddressAlreadyInUse);

        _cts = new CancellationTokenSource();
        _ = AcceptLoopAsync(_listener, _cts.Token);
    }

    public void Stop()
    {
        _cts?.Cancel();
        try { _listener?.Stop(); } catch { }
        _listener = null;
    }

    public void Dispose() => Stop();

    private async Task AcceptLoopAsync(TcpListener listener, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            TcpClient client;
            try { client = await listener.AcceptTcpClientAsync(ct); }
            catch { return; }
            _ = Task.Run(() => HandleClientAsync(client, ct));
        }
    }

    // ------------------------------------------------------------ itens para o iPhone

    public IReadOnlyList<ShareItem> Outbox
    {
        get { lock (_lock) return _outbox.ToList(); }
    }

    public ShareItem OfferFile(string path)
    {
        var info = new FileInfo(path);
        if (!info.Exists) throw new FileNotFoundException("Arquivo não encontrado", path);
        var item = new ShareItem
        {
            Id = NewId(), Name = info.Name, Size = info.Length, Kind = ShareKind.File, Path = info.FullName, Created = DateTime.Now,
        };
        Add(item);
        return item;
    }

    public ShareItem OfferText(string text)
    {
        var item = new ShareItem
        {
            Id = NewId(), Name = text.Length > 60 ? text[..60] + "…" : text, Size = Encoding.UTF8.GetByteCount(text),
            Kind = ShareKind.Text, Text = text, Created = DateTime.Now,
        };
        Add(item);
        return item;
    }

    private void Add(ShareItem item)
    {
        lock (_lock)
        {
            _outbox.Insert(0, item);
            if (_outbox.Count > 200) _outbox.RemoveRange(200, _outbox.Count - 200);
        }
        OutboxChanged?.Invoke();
    }

    public bool Remove(string id)
    {
        bool removed;
        lock (_lock) removed = _outbox.RemoveAll(i => i.Id == id) > 0;
        if (removed) OutboxChanged?.Invoke();
        return removed;
    }

    private ShareItem? Find(string id)
    {
        lock (_lock) return _outbox.FirstOrDefault(i => i.Id == id);
    }

    private void MarkDelivered(ShareItem item)
    {
        if (item.Delivered) return;
        item.Delivered = true;
        ItemDelivered?.Invoke(item);
    }

    private static string NewId() => Convert.ToHexString(RandomNumberGenerator.GetBytes(6)).ToLowerInvariant();

    // ------------------------------------------------------------ HTTP

    private async Task HandleClientAsync(TcpClient client, CancellationToken appCt)
    {
        using var _ = client;
        client.NoDelay = true;
        var remote = (client.Client.RemoteEndPoint as IPEndPoint)?.Address ?? IPAddress.None;
        var conn = new HttpConnection(client.GetStream());

        try
        {
            while (!appCt.IsCancellationRequested)
            {
                HttpRequest? req;
                using (var idle = CancellationTokenSource.CreateLinkedTokenSource(appCt))
                {
                    idle.CancelAfter(TimeSpan.FromSeconds(20));
                    req = await conn.ReadRequestAsync(idle.Token);
                }
                if (req is null) return;

                if (!Network.IsLocalNetwork(remote))
                {
                    await Text(conn.Stream, 403, "Só funciona na rede local.", false);
                    return;
                }

                bool keep = await RouteAsync(req, conn.Stream, appCt) && req.KeepAlive;
                if (!keep) return;
            }
        }
        catch (OperationCanceledException) { }
        catch (IOException) { }
        catch (SocketException) { }
        catch (InvalidDataException)
        {
            try { await Text(conn.Stream, 400, "Pedido inválido.", false); } catch { }
        }
    }

    private bool Authorized(HttpRequest req)
    {
        var key = req.Param("k") ?? req.Header("X-Native-Key") ?? "";
        var a = Encoding.UTF8.GetBytes(key);
        var b = Encoding.UTF8.GetBytes(Token);
        return a.Length == b.Length && CryptographicOperations.FixedTimeEquals(a, b);
    }

    private void Seen()
    {
        Interlocked.Exchange(ref _lastSeenTicks, DateTime.Now.Ticks);
        PhoneSeen?.Invoke();
    }

    /// <summary>Responde o pedido. Retorna falso se a conexão deve ser fechada.</summary>
    private async Task<bool> RouteAsync(HttpRequest req, Stream s, CancellationToken ct)
    {
        string path = req.Path.TrimEnd('/');
        if (path.Length == 0) path = "/";

        if (req.Method == "OPTIONS")
        {
            await HttpResponse.WriteAsync(s, 204, "text/plain", Array.Empty<byte>(), req.KeepAlive, new Dictionary<string, string>
            {
                ["Access-Control-Allow-Methods"] = "GET, POST, OPTIONS",
                ["Access-Control-Allow-Headers"] = "X-Native-Key, X-Filename, Content-Type",
            }, ct);
            return true;
        }

        // públicas
        switch (req.Method, path)
        {
            case ("GET", "/api/ping"):
                await Text(s, 200, "native", req.KeepAlive, ct);
                return true;
            case ("GET", "/") or ("GET", "/index.html"):
                await HttpResponse.WriteAsync(s, 200, "text/html; charset=utf-8", Encoding.UTF8.GetBytes(SharePage.Html), req.KeepAlive, null, ct);
                return true;
            case ("GET", "/icon.png") or ("GET", "/apple-touch-icon.png"):
                if (TouchIcon is null) { await Text(s, 404, "", req.KeepAlive, ct); return true; }
                await HttpResponse.WriteAsync(s, 200, "image/png", TouchIcon, req.KeepAlive, null, ct);
                return true;
            case ("GET", "/favicon.ico"):
                await Text(s, 404, "", req.KeepAlive, ct);
                return true;
        }

        if (!Authorized(req))
        {
            await Json(s, 401, new { error = "chave" }, req.KeepAlive, ct);
            return true;
        }

        if (req.Method == "GET" && path == "/api/state")
        {
            Seen();
            var items = Outbox.Select(i => new
            {
                id = i.Id, name = i.Name, size = i.Size, kind = i.Kind == ShareKind.Text ? "text" : "file",
                text = i.Text, type = i.Kind == ShareKind.File ? MimeTypes.For(i.Name) : "text/plain",
                delivered = i.Delivered, created = new DateTimeOffset(i.Created).ToUnixTimeMilliseconds(),
            });
            var key = Uri.EscapeDataString(Token);
            var shortcut = ShortcutBase is null ? null : $"{ShortcutBase}/api/upload?k={key}";
            var clipUrl = ShortcutBase is null ? null : $"{ShortcutBase}/api/clip?k={key}";
            await Json(s, 200, new { name = DisplayName, shortcut, clipUrl, clipEnabled = ClipboardProvider is not null, items }, req.KeepAlive, ct);
            return true;
        }

        if (req.Method == "GET" && path.StartsWith("/api/file/", StringComparison.Ordinal))
            return await SendFileAsync(req, s, path["/api/file/".Length..].Split('/')[0], ct);

        if (req.Method == "POST" && path.StartsWith("/api/ack/", StringComparison.Ordinal))
        {
            if (Find(path["/api/ack/".Length..]) is { } item) MarkDelivered(item);
            await Json(s, 200, new { ok = true }, req.KeepAlive, ct);
            return true;
        }

        if (req.Method == "POST" && path.StartsWith("/api/remove/", StringComparison.Ordinal))
        {
            Remove(path["/api/remove/".Length..]);
            await Json(s, 200, new { ok = true }, req.KeepAlive, ct);
            return true;
        }

        if (req.Method == "POST" && path == "/api/text")
        {
            Seen();
            using var ms = new MemoryStream();
            await CopyLimitedAsync(req.Body, ms, 1024 * 1024, ct);
            var text = Encoding.UTF8.GetString(ms.ToArray()).Trim();
            if (text.Length > 0) TextReceived?.Invoke(text);
            await Json(s, 200, new { ok = true }, req.KeepAlive, ct);
            return true;
        }

        if (path == "/api/clip")
        {
            Seen();
            if (req.Method == "GET")
            {
                if (ClipboardProvider is null)
                {
                    await Json(s, 403, new { error = "a leitura da área de transferência está desligada no PC" }, req.KeepAlive, ct);
                    return true;
                }
                var clip = await ClipboardProvider();
                if (clip is null || clip.IsEmpty)
                {
                    await HttpResponse.WriteAsync(s, 204, "text/plain", Array.Empty<byte>(), req.KeepAlive, null, ct);
                }
                else if (clip.Image is not null)
                {
                    await HttpResponse.WriteAsync(s, 200, clip.ImageType ?? "image/png", clip.Image, req.KeepAlive,
                        new Dictionary<string, string> { ["Content-Disposition"] = HttpResponse.ContentDisposition("Copiado do PC.png", false) }, ct);
                }
                else
                {
                    await Text(s, 200, clip.Text!, req.KeepAlive, ct);
                }
                return true;
            }
            if (req.Method == "POST")
            {
                using var ms = new MemoryStream();
                await CopyLimitedAsync(req.Body, ms, 40L * 1024 * 1024, ct);
                var clip = ClipData.FromBody(ms.ToArray(), req.Header("Content-Type"));
                if (!clip.IsEmpty) ClipboardReceived?.Invoke(clip);
                await Json(s, 200, new { ok = !clip.IsEmpty }, req.KeepAlive, ct);
                return true;
            }
        }

        if (req.Method == "POST" && path == "/api/upload")
            return await ReceiveFileAsync(req, s, ct);

        await Json(s, 404, new { error = "não encontrado" }, req.KeepAlive, ct);
        return true;
    }

    private async Task<bool> SendFileAsync(HttpRequest req, Stream s, string id, CancellationToken ct)
    {
        var item = Find(id);
        if (item is null || item.Kind != ShareKind.File || item.Path is null || !File.Exists(item.Path))
        {
            await Json(s, 404, new { error = "arquivo não está mais disponível" }, req.KeepAlive, ct);
            return true;
        }

        bool download = req.Param("dl") == "1";
        bool preview = req.Param("t") == "1";
        await using var fs = new FileStream(item.Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 128 * 1024, useAsync: true);
        long size = fs.Length;
        var range = HttpResponse.ParseRange(req.Header("Range"), size);
        long start = range?.Start ?? 0, end = range?.End ?? size - 1;
        long length = size == 0 ? 0 : end - start + 1;

        var headers = new Dictionary<string, string>
        {
            ["Content-Disposition"] = HttpResponse.ContentDisposition(item.Name, download),
            ["Accept-Ranges"] = "bytes",
        };
        if (range is not null) headers["Content-Range"] = $"bytes {start}-{end}/{size}";

        // Para baixar, manda como binário genérico: com o tipo real (PDF, Word...) o iPhone só abre a prévia.
        var contentType = download ? "application/octet-stream" : MimeTypes.For(item.Name);
        await HttpResponse.WriteHeadAsync(s, range is null ? 200 : 206, contentType, length, req.KeepAlive, headers, ct);
        if (req.Method == "HEAD" || length == 0) { await s.FlushAsync(ct); return true; }

        fs.Position = start;
        var buffer = new byte[128 * 1024];
        long left = length;
        while (left > 0)
        {
            int n = await fs.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, left)), ct);
            if (n <= 0) break;
            await s.WriteAsync(buffer.AsMemory(0, n), ct);
            left -= n;
        }
        await s.FlushAsync(ct);

        if (!preview && end == size - 1 && download) MarkDelivered(item);
        return true;
    }

    private async Task<bool> ReceiveFileAsync(HttpRequest req, Stream s, CancellationToken ct)
    {
        Seen();
        var contentType = req.Header("Content-Type");
        var rawName = req.Param("name") ?? req.Header("X-Filename");
        var name = FileNames.Clean(rawName, contentType, DateTime.Now);

        string folder;
        try
        {
            folder = _receiveFolder();
            Directory.CreateDirectory(folder);
        }
        catch (Exception ex)
        {
            await Json(s, 500, new { error = "pasta de destino: " + ex.Message }, false, ct);
            return false;
        }

        var temp = System.IO.Path.Combine(folder, $".native-{NewId()}.part");
        long written;
        try
        {
            await using (var fs = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 256 * 1024, useAsync: true))
            {
                await req.Body.CopyToAsync(fs, 256 * 1024, ct);
                written = fs.Length;
            }
        }
        catch (Exception ex)
        {
            try { File.Delete(temp); } catch { }
            if (ex is IOException { HResult: unchecked((int)0x80070070) })   // disco cheio
            {
                await Json(s, 507, new { error = "disco do PC cheio" }, false, ct);
            }
            return false;
        }

        if (written == 0)
        {
            File.Delete(temp);
            await Json(s, 400, new { error = "arquivo vazio" }, req.KeepAlive, ct);
            return true;
        }

        var final = FileNames.Unique(folder, name);
        File.Move(temp, final);
        var received = new ReceivedFile(final, System.IO.Path.GetFileName(final), written, contentType);
        FileReceived?.Invoke(received);
        await Json(s, 200, new { ok = true, name = received.Name, size = written }, req.KeepAlive, ct);
        return true;
    }

    private static async Task CopyLimitedAsync(Stream from, Stream to, long max, CancellationToken ct)
    {
        var buf = new byte[16 * 1024];
        long total = 0;
        int n;
        while ((n = await from.ReadAsync(buf, ct)) > 0)
        {
            total += n;
            if (total > max) throw new InvalidDataException("texto grande demais");
            await to.WriteAsync(buf.AsMemory(0, n), ct);
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private static Task Json(Stream s, int status, object value, bool keepAlive, CancellationToken ct = default) =>
        HttpResponse.WriteAsync(s, status, "application/json; charset=utf-8", JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions), keepAlive, null, ct);

    private static Task Text(Stream s, int status, string text, bool keepAlive, CancellationToken ct = default) =>
        HttpResponse.WriteAsync(s, status, "text/plain; charset=utf-8", Encoding.UTF8.GetBytes(text), keepAlive, null, ct);
}

/// <summary>Nomes de arquivo seguros para o Windows.</summary>
public static class FileNames
{
    private static readonly char[] Invalid = "<>:\"/\\|?*".ToCharArray();
    private static readonly HashSet<string> Reserved = new(StringComparer.OrdinalIgnoreCase)
        { "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
          "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9" };

    public static string Clean(string? raw, string? contentType, DateTime now)
    {
        var name = raw ?? "";
        if (name.Contains('%'))
        {
            try { name = Uri.UnescapeDataString(name); } catch (UriFormatException) { }
        }
        // só o nome, nunca um caminho
        name = name.Replace('\\', '/');
        name = name[(name.LastIndexOf('/') + 1)..];

        var sb = new StringBuilder();
        foreach (var ch in name)
            sb.Append(ch < ' ' || Array.IndexOf(Invalid, ch) >= 0 ? '_' : ch);
        name = sb.ToString().Trim().TrimEnd('.', ' ').TrimStart('.');

        var ext = MimeTypes.ExtensionFor(contentType);
        if (name.Length == 0)
        {
            string kind = contentType?.StartsWith("image/", StringComparison.OrdinalIgnoreCase) == true ? "Foto"
                        : contentType?.StartsWith("video/", StringComparison.OrdinalIgnoreCase) == true ? "Vídeo" : "Arquivo";
            name = $"{kind} do iPhone {now.ToString("yyyy-MM-dd HH.mm.ss", CultureInfo.InvariantCulture)}";
        }
        if (!HasRealExtension(name) && ext is not null) name += ext;

        var stem = System.IO.Path.GetFileNameWithoutExtension(name);
        if (Reserved.Contains(stem)) name = "_" + name;
        if (name.Length > 150)
        {
            var e = System.IO.Path.GetExtension(name);
            name = name[..(150 - e.Length)] + e;
        }
        return name;
    }

    private static bool HasRealExtension(string name)
    {
        var e = System.IO.Path.GetExtension(name);
        return e.Length is >= 2 and <= 6 && e.Skip(1).Any(char.IsLetter) && !e.Contains(' ');
    }

    /// <summary>"foto.jpg" → "foto (2).jpg" se já existir.</summary>
    public static string Unique(string folder, string name)
    {
        var path = System.IO.Path.Combine(folder, name);
        if (!File.Exists(path)) return path;
        var stem = System.IO.Path.GetFileNameWithoutExtension(name);
        var ext = System.IO.Path.GetExtension(name);
        for (int i = 2; ; i++)
        {
            path = System.IO.Path.Combine(folder, $"{stem} ({i}){ext}");
            if (!File.Exists(path)) return path;
        }
    }
}

public static class MimeTypes
{
    private static readonly Dictionary<string, string> ByExt = new(StringComparer.OrdinalIgnoreCase)
    {
        [".jpg"] = "image/jpeg", [".jpeg"] = "image/jpeg", [".png"] = "image/png", [".gif"] = "image/gif",
        [".webp"] = "image/webp", [".heic"] = "image/heic", [".heif"] = "image/heif", [".bmp"] = "image/bmp", [".svg"] = "image/svg+xml",
        [".mp4"] = "video/mp4", [".m4v"] = "video/x-m4v", [".mov"] = "video/quicktime", [".webm"] = "video/webm",
        [".mp3"] = "audio/mpeg", [".m4a"] = "audio/mp4", [".wav"] = "audio/wav", [".aac"] = "audio/aac",
        [".pdf"] = "application/pdf", [".txt"] = "text/plain; charset=utf-8", [".csv"] = "text/csv; charset=utf-8",
        [".html"] = "text/html; charset=utf-8", [".json"] = "application/json", [".zip"] = "application/zip",
        [".doc"] = "application/msword", [".xls"] = "application/vnd.ms-excel", [".ppt"] = "application/vnd.ms-powerpoint",
        [".docx"] = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        [".xlsx"] = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        [".pptx"] = "application/vnd.openxmlformats-officedocument.presentationml.presentation",
        [".pages"] = "application/vnd.apple.pages", [".numbers"] = "application/vnd.apple.numbers", [".key"] = "application/vnd.apple.keynote",
        [".vcf"] = "text/vcard", [".ics"] = "text/calendar",
    };

    public static string For(string fileName) =>
        ByExt.TryGetValue(System.IO.Path.GetExtension(fileName), out var t) ? t : "application/octet-stream";

    public static string? ExtensionFor(string? contentType)
    {
        if (string.IsNullOrEmpty(contentType)) return null;
        var bare = contentType.Split(';')[0].Trim();
        if (bare.Equals("image/jpeg", StringComparison.OrdinalIgnoreCase)) return ".jpg";
        foreach (var kv in ByExt)
            if (kv.Value.Split(';')[0].Equals(bare, StringComparison.OrdinalIgnoreCase)) return kv.Key;
        return null;
    }
}
