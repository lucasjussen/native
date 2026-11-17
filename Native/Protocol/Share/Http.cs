using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Native.Protocol.Share;

/// <summary>Pedido HTTP/1.1 já com cabeçalhos lidos; o corpo é lido sob demanda.</summary>
public sealed class HttpRequest
{
    public string Method { get; init; } = "GET";
    public string Path { get; init; } = "/";
    public Dictionary<string, string> Query { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> Headers { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public Stream Body { get; init; } = Stream.Null;
    public long? ContentLength { get; init; }
    public bool KeepAlive { get; init; }

    public string? Header(string name) => Headers.TryGetValue(name, out var v) ? v : null;
    public string? Param(string name) => Query.TryGetValue(name, out var v) ? v : null;
}

/// <summary>Leitura bufferizada de uma conexão HTTP (cabeçalhos + corpo com Content-Length ou chunked).</summary>
public sealed class HttpConnection
{
    private readonly Stream _stream;
    private readonly byte[] _buf = new byte[64 * 1024];
    private int _pos, _len;
    private Stream? _openBody;

    public const int MaxHeaderBytes = 32 * 1024;

    public HttpConnection(Stream stream) => _stream = stream;

    public Stream Stream => _stream;

    private async ValueTask<bool> FillAsync(CancellationToken ct)
    {
        if (_pos > 0 && _pos == _len) { _pos = _len = 0; }
        if (_len == _buf.Length)
        {
            if (_pos == 0) return false;
            Buffer.BlockCopy(_buf, _pos, _buf, 0, _len - _pos);
            _len -= _pos;
            _pos = 0;
        }
        int n = await _stream.ReadAsync(_buf.AsMemory(_len, _buf.Length - _len), ct);
        if (n <= 0) return false;
        _len += n;
        return true;
    }

    /// <summary>Lê no máximo <paramref name="max"/> bytes já disponíveis (ou espera chegar algo).</summary>
    internal async ValueTask<int> ReadRawAsync(Memory<byte> dest, CancellationToken ct)
    {
        if (_pos == _len && !await FillAsync(ct)) return 0;
        int n = Math.Min(dest.Length, _len - _pos);
        _buf.AsMemory(_pos, n).CopyTo(dest);
        _pos += n;
        return n;
    }

    internal async ValueTask<string?> ReadLineAsync(CancellationToken ct, int max = 8192)
    {
        var sb = new List<byte>();
        while (true)
        {
            if (_pos == _len && !await FillAsync(ct)) return sb.Count == 0 ? null : Encoding.UTF8.GetString(sb.ToArray());
            byte b = _buf[_pos++];
            if (b == '\n')
            {
                if (sb.Count > 0 && sb[^1] == '\r') sb.RemoveAt(sb.Count - 1);
                return Encoding.UTF8.GetString(sb.ToArray());
            }
            sb.Add(b);
            if (sb.Count > max) throw new InvalidDataException("linha grande demais");
        }
    }

    /// <summary>Lê o próximo pedido. Nulo se a conexão foi fechada.</summary>
    public async Task<HttpRequest?> ReadRequestAsync(CancellationToken ct)
    {
        // Termina de ler o corpo do pedido anterior, se o código não leu tudo.
        if (_openBody is not null)
        {
            await _openBody.CopyToAsync(Stream.Null, ct);
            _openBody = null;
        }

        string? line;
        do { line = await ReadLineAsync(ct); if (line is null) return null; } while (line.Length == 0);

        var parts = line.Split(' ');
        if (parts.Length < 3 || !parts[2].StartsWith("HTTP/1.", StringComparison.Ordinal)) throw new InvalidDataException("pedido inválido");
        string method = parts[0].ToUpperInvariant();
        string target = parts[1];
        bool http10 = parts[2] == "HTTP/1.0";

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        int total = 0;
        while (true)
        {
            var h = await ReadLineAsync(ct) ?? throw new InvalidDataException("cabeçalho incompleto");
            if (h.Length == 0) break;
            total += h.Length;
            if (total > MaxHeaderBytes) throw new InvalidDataException("cabeçalhos grandes demais");
            int colon = h.IndexOf(':');
            if (colon <= 0) continue;
            var name = h[..colon].Trim();
            var value = h[(colon + 1)..].Trim();
            headers[name] = headers.TryGetValue(name, out var prev) ? prev + ", " + value : value;
        }

        string path = target, query = "";
        int q = target.IndexOf('?');
        if (q >= 0) { path = target[..q]; query = target[(q + 1)..]; }

        long? length = null;
        if (headers.TryGetValue("Content-Length", out var cl) && long.TryParse(cl, NumberStyles.None, CultureInfo.InvariantCulture, out var n)) length = n;
        bool chunked = headers.TryGetValue("Transfer-Encoding", out var te) && te.Contains("chunked", StringComparison.OrdinalIgnoreCase);

        Stream body = chunked ? new ChunkedBody(this) : length is > 0 ? new LengthBody(this, length.Value) : Stream.Null;
        _openBody = body == Stream.Null ? null : body;

        var conn = headers.TryGetValue("Connection", out var c) ? c : "";
        bool keepAlive = http10 ? conn.Contains("keep-alive", StringComparison.OrdinalIgnoreCase)
                                : !conn.Contains("close", StringComparison.OrdinalIgnoreCase);

        return new HttpRequest
        {
            Method = method,
            Path = Uri.UnescapeDataString(path),
            Query = ParseQuery(query),
            Headers = headers,
            Body = body,
            ContentLength = chunked ? null : length ?? 0,
            KeepAlive = keepAlive,
        };
    }

    public static Dictionary<string, string> ParseQuery(string query)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            int eq = pair.IndexOf('=');
            string k = eq < 0 ? pair : pair[..eq], v = eq < 0 ? "" : pair[(eq + 1)..];
            try { result[Uri.UnescapeDataString(k.Replace('+', ' '))] = Uri.UnescapeDataString(v.Replace('+', ' ')); }
            catch (UriFormatException) { }
        }
        return result;
    }

    private sealed class LengthBody : Stream
    {
        private readonly HttpConnection _c;
        private long _left;
        public LengthBody(HttpConnection c, long length) { _c = c; _left = length; }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        {
            if (_left <= 0) return 0;
            int n = await _c.ReadRawAsync(buffer[..(int)Math.Min(buffer.Length, _left)], ct);
            if (n == 0) throw new IOException("conexão fechada no meio do envio");
            _left -= n;
            return n;
        }
        public override Task<int> ReadAsync(byte[] b, int o, int c, CancellationToken ct) => ReadAsync(b.AsMemory(o, c), ct).AsTask();
        public override int Read(byte[] b, int o, int c) => ReadAsync(b, o, c, CancellationToken.None).GetAwaiter().GetResult();
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long o, SeekOrigin s) => throw new NotSupportedException();
        public override void SetLength(long v) => throw new NotSupportedException();
        public override void Write(byte[] b, int o, int c) => throw new NotSupportedException();
    }

    private sealed class ChunkedBody : Stream
    {
        private readonly HttpConnection _c;
        private long _chunkLeft;
        private bool _done;
        public ChunkedBody(HttpConnection c) => _c = c;
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        {
            if (_done) return 0;
            if (_chunkLeft == 0)
            {
                var line = await _c.ReadLineAsync(ct) ?? throw new IOException("chunk incompleto");
                int semi = line.IndexOf(';');
                if (semi >= 0) line = line[..semi];
                _chunkLeft = long.Parse(line.Trim(), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                if (_chunkLeft == 0)
                {
                    while (!string.IsNullOrEmpty(await _c.ReadLineAsync(ct))) { }   // trailers
                    _done = true;
                    return 0;
                }
            }
            int n = await _c.ReadRawAsync(buffer[..(int)Math.Min(buffer.Length, _chunkLeft)], ct);
            if (n == 0) throw new IOException("conexão fechada no meio do envio");
            _chunkLeft -= n;
            if (_chunkLeft == 0) await _c.ReadLineAsync(ct);   // \r\n depois do chunk
            return n;
        }
        public override Task<int> ReadAsync(byte[] b, int o, int c, CancellationToken ct) => ReadAsync(b.AsMemory(o, c), ct).AsTask();
        public override int Read(byte[] b, int o, int c) => ReadAsync(b, o, c, CancellationToken.None).GetAwaiter().GetResult();
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long o, SeekOrigin s) => throw new NotSupportedException();
        public override void SetLength(long v) => throw new NotSupportedException();
        public override void Write(byte[] b, int o, int c) => throw new NotSupportedException();
    }
}

public static class HttpResponse
{
    public static string StatusText(int code) => code switch
    {
        200 => "OK", 204 => "No Content", 206 => "Partial Content", 400 => "Bad Request", 401 => "Unauthorized",
        403 => "Forbidden", 404 => "Not Found", 405 => "Method Not Allowed", 411 => "Length Required",
        413 => "Payload Too Large", 416 => "Range Not Satisfiable", 500 => "Internal Server Error", 507 => "Insufficient Storage",
        _ => "OK",
    };

    public static async Task WriteHeadAsync(Stream s, int status, string? contentType, long? length, bool keepAlive,
        IEnumerable<KeyValuePair<string, string>>? extra = null, CancellationToken ct = default)
    {
        var sb = new StringBuilder();
        sb.Append("HTTP/1.1 ").Append(status).Append(' ').Append(StatusText(status)).Append("\r\n");
        if (contentType is not null) sb.Append("Content-Type: ").Append(contentType).Append("\r\n");
        if (length is not null) sb.Append("Content-Length: ").Append(length.Value.ToString(CultureInfo.InvariantCulture)).Append("\r\n");
        sb.Append("Cache-Control: no-store\r\n");
        sb.Append("Access-Control-Allow-Origin: *\r\n");
        sb.Append("X-Content-Type-Options: nosniff\r\n");
        sb.Append("Connection: ").Append(keepAlive ? "keep-alive" : "close").Append("\r\n");
        if (extra is not null)
            foreach (var h in extra) sb.Append(h.Key).Append(": ").Append(h.Value).Append("\r\n");
        sb.Append("\r\n");
        var bytes = Encoding.UTF8.GetBytes(sb.ToString());
        await s.WriteAsync(bytes, ct);
    }

    public static async Task WriteAsync(Stream s, int status, string contentType, byte[] body, bool keepAlive,
        IEnumerable<KeyValuePair<string, string>>? extra = null, CancellationToken ct = default)
    {
        await WriteHeadAsync(s, status, contentType, body.Length, keepAlive, extra, ct);
        if (body.Length > 0) await s.WriteAsync(body, ct);
        await s.FlushAsync(ct);
    }

    /// <summary>Content-Disposition com nome UTF-8 (RFC 6266), para acentos chegarem certos no iPhone.</summary>
    public static string ContentDisposition(string fileName, bool attachment)
    {
        var ascii = new StringBuilder();
        foreach (var ch in fileName) ascii.Append(ch is >= ' ' and < (char)127 and not '"' and not '\\' ? ch : '_');
        return $"{(attachment ? "attachment" : "inline")}; filename=\"{ascii}\"; filename*=UTF-8''{Uri.EscapeDataString(fileName)}";
    }

    /// <summary>Lê "bytes=a-b" (só um intervalo). Nulo se não houver ou for inválido.</summary>
    public static (long Start, long End)? ParseRange(string? header, long size)
    {
        if (header is null || !header.StartsWith("bytes=", StringComparison.OrdinalIgnoreCase) || size <= 0) return null;
        var spec = header[6..].Split(',')[0].Trim();
        int dash = spec.IndexOf('-');
        if (dash < 0) return null;
        var a = spec[..dash]; var b = spec[(dash + 1)..];
        long start, end;
        if (a.Length == 0)
        {
            if (!long.TryParse(b, out var suffix) || suffix <= 0) return null;
            start = Math.Max(0, size - suffix);
            end = size - 1;
        }
        else
        {
            if (!long.TryParse(a, out start)) return null;
            end = b.Length == 0 ? size - 1 : long.TryParse(b, out var e) ? Math.Min(e, size - 1) : -1;
        }
        if (start < 0 || start >= size || end < start) return null;
        return (start, end);
    }
}
