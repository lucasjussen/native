using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Native.Protocol;

// OBEX (IrDA Object Exchange) sobre um canal de bytes. É a base do MAP (mensagens) e do
// PBAP (contatos). Este arquivo não depende de Windows: o transporte é uma interface,
// o que permite testar cliente e servidor em memória.

public interface IObexTransport : IDisposable
{
    Task WriteAsync(byte[] data, CancellationToken ct);
    Task<byte[]> ReadExactAsync(int count, CancellationToken ct);
}

public static class ObexOp
{
    public const byte Connect = 0x80, Disconnect = 0x81, Put = 0x02, Get = 0x03, SetPath = 0x85, Abort = 0xFF;
    public const byte Final = 0x80;
}

public static class ObexStatus
{
    public const byte Continue = 0x90, Success = 0xA0, BadRequest = 0xC0, Unauthorized = 0xC1, Forbidden = 0xC3,
        NotFound = 0xC4, NotAcceptable = 0xC6, PreconditionFailed = 0xCC, InternalError = 0xD0,
        NotImplemented = 0xD1, ServiceUnavailable = 0xD3;

    public static string Describe(byte code) => code switch
    {
        BadRequest => "o iPhone não entendeu o pedido",
        Unauthorized => "o iPhone não autorizou",
        Forbidden => "o iPhone negou o acesso",
        NotFound => "não encontrado no iPhone",
        NotAcceptable => "o iPhone não aceitou",
        PreconditionFailed => "o iPhone recusou neste momento",
        InternalError => "erro interno do iPhone",
        NotImplemented => "o iPhone não oferece essa função",
        ServiceUnavailable => "serviço indisponível no iPhone",
        _ => $"código 0x{code:X2}",
    };
}

public readonly struct ObexHeader
{
    public const byte Name = 0x01, Type = 0x42, Target = 0x46, Body = 0x48, EndOfBody = 0x49, Who = 0x4A,
        AppParameters = 0x4C, Length = 0xC3, ConnectionId = 0xCB;

    public byte Id { get; }
    public byte[] Value { get; }

    public ObexHeader(byte id, byte[] value)
    {
        Id = id;
        Value = value;
    }

    /// <summary>Texto Unicode (UTF-16 big-endian com terminador). Texto vazio vira um cabeçalho sem valor.</summary>
    public static ObexHeader Text(byte id, string value) =>
        new(id, value.Length == 0 ? Array.Empty<byte>() : Encoding.BigEndianUnicode.GetBytes(value + "\0"));

    public static ObexHeader Ascii(byte id, string value) => new(id, Encoding.ASCII.GetBytes(value + "\0"));

    public static ObexHeader UInt32(byte id, uint value) =>
        new(id, new[] { (byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value });

    public string AsText() => Encoding.BigEndianUnicode.GetString(Value).TrimEnd('\0');
    public string AsAscii() => Encoding.ASCII.GetString(Value).TrimEnd('\0');
    public uint AsUInt32() => Value.Length == 4 ? (uint)((Value[0] << 24) | (Value[1] << 16) | (Value[2] << 8) | Value[3]) : 0;

    public int EncodedSize => (Id & 0xC0) switch
    {
        0x80 => 2,
        0xC0 => 5,
        _ => 3 + Value.Length,
    };

    public void WriteTo(List<byte> buffer)
    {
        buffer.Add(Id);
        switch (Id & 0xC0)
        {
            case 0x80:
                buffer.Add(Value.Length > 0 ? Value[0] : (byte)0);
                break;
            case 0xC0:
                for (int i = 0; i < 4; i++) buffer.Add(i < Value.Length ? Value[i] : (byte)0);
                break;
            default:
                int length = Value.Length + 3;
                buffer.Add((byte)(length >> 8));
                buffer.Add((byte)length);
                buffer.AddRange(Value);
                break;
        }
    }
}

public static class ObexCodec
{
    public static List<ObexHeader> ParseHeaders(byte[] data, int offset)
    {
        var headers = new List<ObexHeader>();
        int i = offset;
        while (i < data.Length)
        {
            byte id = data[i];
            switch (id & 0xC0)
            {
                case 0x80:
                    if (i + 2 > data.Length) return headers;
                    headers.Add(new ObexHeader(id, new[] { data[i + 1] }));
                    i += 2;
                    break;
                case 0xC0:
                    if (i + 5 > data.Length) return headers;
                    headers.Add(new ObexHeader(id, data.AsSpan(i + 1, 4).ToArray()));
                    i += 5;
                    break;
                default:
                    if (i + 3 > data.Length) return headers;
                    int length = (data[i + 1] << 8) | data[i + 2];
                    if (length < 3 || i + length > data.Length) return headers;
                    headers.Add(new ObexHeader(id, data.AsSpan(i + 3, length - 3).ToArray()));
                    i += length;
                    break;
            }
        }
        return headers;
    }

    public static void Seal(List<byte> packet)
    {
        if (packet.Count > 0xFFFF) throw new InvalidOperationException("Pacote OBEX grande demais.");
        packet[1] = (byte)(packet.Count >> 8);
        packet[2] = (byte)packet.Count;
    }
}

/// <summary>Parâmetros de aplicação (lista de tag, tamanho, valor) usados por MAP e PBAP.</summary>
public static class ObexAppParams
{
    public static byte[] Build(params (byte Tag, byte[] Value)[] items)
    {
        var buffer = new List<byte>();
        foreach (var (tag, value) in items)
        {
            buffer.Add(tag);
            buffer.Add((byte)value.Length);
            buffer.AddRange(value);
        }
        return buffer.ToArray();
    }

    public static Dictionary<byte, byte[]> Parse(byte[]? data)
    {
        var result = new Dictionary<byte, byte[]>();
        if (data is null) return result;
        int i = 0;
        while (i + 2 <= data.Length)
        {
            byte tag = data[i];
            int length = data[i + 1];
            if (i + 2 + length > data.Length) break;
            result[tag] = data.AsSpan(i + 2, length).ToArray();
            i += 2 + length;
        }
        return result;
    }

    public static byte[] U8(byte value) => new[] { value };
    public static byte[] U16(int value) => new[] { (byte)(value >> 8), (byte)value };
    public static byte[] U64(ulong value)
    {
        var bytes = new byte[8];
        for (int i = 0; i < 8; i++) bytes[i] = (byte)(value >> (56 - 8 * i));
        return bytes;
    }

    public static int ReadU16(byte[] value) => value.Length >= 2 ? (value[0] << 8) | value[1] : 0;
}

public sealed class ObexException : Exception
{
    public byte Code { get; }

    public ObexException(byte code, string operation) : base($"{operation}: {ObexStatus.Describe(code)}")
    {
        Code = code;
    }
}

public sealed class ObexResult
{
    public List<ObexHeader> Headers { get; } = new();
    public byte[] Body { get; set; } = Array.Empty<byte>();

    public ObexHeader? Find(byte id)
    {
        foreach (var h in Headers)
            if (h.Id == id) return h;
        return null;
    }
}

/// <summary>Lado cliente do OBEX: uma operação por vez, com corpo fatiado conforme o tamanho negociado.</summary>
public sealed class ObexClient : IDisposable
{
    private const int OurMaxPacket = 0x4000;

    private readonly IObexTransport _transport;
    private readonly TimeSpan _timeout;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private uint? _connectionId;

    public ObexClient(IObexTransport transport, TimeSpan? timeout = null)
    {
        _transport = transport;
        _timeout = timeout ?? TimeSpan.FromSeconds(15);
    }

    public int MaxPacketSize { get; private set; } = 255;

    public async Task ConnectAsync(byte[]? target, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var packet = new List<byte> { ObexOp.Connect, 0, 0, 0x10, 0x00, OurMaxPacket >> 8, OurMaxPacket & 0xFF };
            if (target is not null) new ObexHeader(ObexHeader.Target, target).WriteTo(packet);

            var (code, data) = await ExchangeAsync(packet, ct);
            if (code != ObexStatus.Success) throw new ObexException(code, "Conexão");
            if (data.Length < 7) throw new InvalidDataException("Resposta de conexão OBEX incompleta.");

            MaxPacketSize = Math.Clamp((data[5] << 8) | data[6], 255, 0xFFFF);
            foreach (var h in ObexCodec.ParseHeaders(data, 7))
                if (h.Id == ObexHeader.ConnectionId) _connectionId = h.AsUInt32();
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Entra numa pasta (nome vazio = raiz). Com <paramref name="goUp"/>, sobe um nível antes.</summary>
    public async Task SetPathAsync(string name, bool goUp = false, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var packet = new List<byte> { ObexOp.SetPath, 0, 0, (byte)(0x02 | (goUp ? 0x01 : 0x00)), 0x00 };
            AddConnectionId(packet);
            ObexHeader.Text(ObexHeader.Name, name).WriteTo(packet);

            var (code, _) = await ExchangeAsync(packet, ct);
            if (code != ObexStatus.Success) throw new ObexException(code, $"Abrir a pasta \"{name}\"");
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<ObexResult> GetAsync(IEnumerable<ObexHeader> headers, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var result = new ObexResult();
            using var body = new MemoryStream();

            var packet = NewPacket(ObexOp.Get | ObexOp.Final);
            foreach (var h in headers) h.WriteTo(packet);

            while (true)
            {
                var (code, data) = await ExchangeAsync(packet, ct);
                foreach (var h in ObexCodec.ParseHeaders(data, 3))
                {
                    if (h.Id is ObexHeader.Body or ObexHeader.EndOfBody) body.Write(h.Value, 0, h.Value.Length);
                    else result.Headers.Add(h);
                }

                if (code == ObexStatus.Success) break;
                if (code != ObexStatus.Continue) throw new ObexException(code, "Leitura");
                packet = NewPacket(ObexOp.Get | ObexOp.Final);
            }

            result.Body = body.ToArray();
            return result;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<ObexResult> PutAsync(IEnumerable<ObexHeader> headers, byte[] body, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var packet = NewPacket(ObexOp.Put);
            foreach (var h in headers) h.WriteTo(packet);

            int offset = 0;
            while (true)
            {
                int room = MaxPacketSize - packet.Count - 3;
                if (room < 0) throw new InvalidOperationException("Cabeçalhos OBEX não cabem no pacote negociado.");

                int chunk = Math.Min(room, body.Length - offset);
                bool last = offset + chunk >= body.Length;
                new ObexHeader(last ? ObexHeader.EndOfBody : ObexHeader.Body, body.AsSpan(offset, chunk).ToArray()).WriteTo(packet);
                packet[0] = last ? (byte)(ObexOp.Put | ObexOp.Final) : ObexOp.Put;

                var (code, data) = await ExchangeAsync(packet, ct);
                offset += chunk;

                if (last)
                {
                    if (code != ObexStatus.Success) throw new ObexException(code, "Envio");
                    var result = new ObexResult();
                    result.Headers.AddRange(ObexCodec.ParseHeaders(data, 3));
                    return result;
                }

                if (code != ObexStatus.Continue) throw new ObexException(code, "Envio");
                packet = NewPacket(ObexOp.Put);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task DisconnectAsync()
    {
        if (!await _gate.WaitAsync(2000)) return;
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            await ExchangeAsync(NewPacket(ObexOp.Disconnect), cts.Token);
        }
        catch
        {
            // O outro lado pode já ter fechado; não importa.
        }
        finally
        {
            _gate.Release();
        }
    }

    private List<byte> NewPacket(byte opcode)
    {
        var packet = new List<byte> { opcode, 0, 0 };
        AddConnectionId(packet);
        return packet;
    }

    private void AddConnectionId(List<byte> packet)
    {
        if (_connectionId is uint id) ObexHeader.UInt32(ObexHeader.ConnectionId, id).WriteTo(packet);
    }

    private async Task<(byte Code, byte[] Data)> ExchangeAsync(List<byte> packet, CancellationToken ct)
    {
        ObexCodec.Seal(packet);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(_timeout);
        try
        {
            await _transport.WriteAsync(packet.ToArray(), cts.Token);

            var head = await _transport.ReadExactAsync(3, cts.Token);
            int length = (head[1] << 8) | head[2];
            if (length < 3) throw new InvalidDataException("Resposta OBEX com tamanho inválido.");

            var data = new byte[length];
            head.CopyTo(data, 0);
            if (length > 3)
            {
                var rest = await _transport.ReadExactAsync(length - 3, cts.Token);
                rest.CopyTo(data, 3);
            }
            return ((byte)(head[0] | ObexOp.Final), data);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new TimeoutException("O iPhone não respondeu a tempo.");
        }
    }

    public void Dispose() => _transport.Dispose();
}

public sealed class ObexRequest
{
    /// <summary>ObexOp.Put, ObexOp.Get ou ObexOp.SetPath (sem o bit final).</summary>
    public byte Opcode { get; init; }
    public byte Flags { get; init; }
    public List<ObexHeader> Headers { get; } = new();
    public byte[] Body { get; set; } = Array.Empty<byte>();

    public string? Name => Find(ObexHeader.Name)?.AsText();
    public string? Type => Find(ObexHeader.Type)?.AsAscii();
    public byte[]? AppParameters => Find(ObexHeader.AppParameters)?.Value;

    private ObexHeader? Find(byte id)
    {
        foreach (var h in Headers)
            if (h.Id == id) return h;
        return null;
    }
}

public sealed class ObexReply
{
    public byte Code { get; init; } = ObexStatus.Success;
    public List<ObexHeader> Headers { get; } = new();
    public byte[] Body { get; init; } = Array.Empty<byte>();

    public static ObexReply Ok() => new();
    public static ObexReply Fail(byte code) => new() { Code = code };
}

/// <summary>
/// Lado servidor do OBEX para uma conexão. O app usa isto para receber os avisos de mensagem
/// nova do iPhone (MNS); os testes usam para simular o iPhone.
/// </summary>
public sealed class ObexServerSession
{
    private readonly IObexTransport _transport;
    private readonly byte[]? _serviceUuid;
    private readonly Func<ObexRequest, Task<ObexReply>> _handler;
    private readonly int _ourMaxPacket;
    private int _peerMaxPacket = 255;

    public ObexServerSession(IObexTransport transport, byte[]? serviceUuid,
        Func<ObexRequest, Task<ObexReply>> handler, int maxPacket = 0x4000)
    {
        _transport = transport;
        _serviceUuid = serviceUuid;
        _handler = handler;
        _ourMaxPacket = maxPacket;
    }

    public async Task RunAsync(CancellationToken ct)
    {
        ObexRequest? pending = null;
        MemoryStream? pendingBody = null;
        byte[]? getBody = null;
        List<ObexHeader>? getHeaders = null;
        int getOffset = 0;

        while (!ct.IsCancellationRequested)
        {
            byte[] data;
            try
            {
                var head = await _transport.ReadExactAsync(3, ct);
                int length = (head[1] << 8) | head[2];
                if (length < 3) return;
                data = new byte[length];
                head.CopyTo(data, 0);
                if (length > 3) (await _transport.ReadExactAsync(length - 3, ct)).CopyTo(data, 3);
            }
            catch (Exception) when (!ct.IsCancellationRequested)
            {
                return;   // o outro lado fechou a conexão
            }

            byte opcode = data[0];
            switch (opcode)
            {
                case ObexOp.Connect:
                {
                    if (data.Length >= 7) _peerMaxPacket = Math.Clamp((data[5] << 8) | data[6], 255, 0xFFFF);
                    var packet = new List<byte> { ObexStatus.Success, 0, 0, 0x10, 0x00, (byte)(_ourMaxPacket >> 8), (byte)_ourMaxPacket };
                    ObexHeader.UInt32(ObexHeader.ConnectionId, 1).WriteTo(packet);
                    if (_serviceUuid is not null) new ObexHeader(ObexHeader.Who, _serviceUuid).WriteTo(packet);
                    ObexCodec.Seal(packet);
                    await _transport.WriteAsync(packet.ToArray(), ct);
                    break;
                }

                case ObexOp.Disconnect:
                    await SendAsync(ObexStatus.Success, null, ct);
                    return;

                case ObexOp.Abort:
                    pending = null;
                    pendingBody = null;
                    getBody = null;
                    await SendAsync(ObexStatus.Success, null, ct);
                    break;

                case ObexOp.SetPath:
                {
                    var request = new ObexRequest { Opcode = ObexOp.SetPath, Flags = data.Length > 3 ? data[3] : (byte)0 };
                    request.Headers.AddRange(ObexCodec.ParseHeaders(data, 5));
                    var reply = await _handler(request);
                    await SendAsync(reply.Code, reply.Headers, ct);
                    break;
                }

                case ObexOp.Put:
                case ObexOp.Put | ObexOp.Final:
                {
                    pending ??= new ObexRequest { Opcode = ObexOp.Put };
                    pendingBody ??= new MemoryStream();
                    foreach (var h in ObexCodec.ParseHeaders(data, 3))
                    {
                        if (h.Id is ObexHeader.Body or ObexHeader.EndOfBody) pendingBody.Write(h.Value, 0, h.Value.Length);
                        else if (h.Id != ObexHeader.ConnectionId) pending.Headers.Add(h);
                    }

                    if ((opcode & ObexOp.Final) == 0)
                    {
                        await SendAsync(ObexStatus.Continue, null, ct);
                        break;
                    }

                    pending.Body = pendingBody.ToArray();
                    var request = pending;
                    pending = null;
                    pendingBody = null;
                    var reply = await _handler(request);
                    await SendAsync(reply.Code, reply.Headers, ct);
                    break;
                }

                case ObexOp.Get:
                case ObexOp.Get | ObexOp.Final:
                {
                    if (getBody is null)
                    {
                        pending ??= new ObexRequest { Opcode = ObexOp.Get };
                        foreach (var h in ObexCodec.ParseHeaders(data, 3))
                            if (h.Id != ObexHeader.ConnectionId) pending.Headers.Add(h);

                        if ((opcode & ObexOp.Final) == 0)
                        {
                            await SendAsync(ObexStatus.Continue, null, ct);
                            break;
                        }

                        var request = pending;
                        pending = null;
                        var reply = await _handler(request);
                        if (reply.Code != ObexStatus.Success)
                        {
                            await SendAsync(reply.Code, reply.Headers, ct);
                            break;
                        }

                        getBody = reply.Body;
                        getHeaders = reply.Headers;
                        getOffset = 0;
                    }

                    var headers = new List<ObexHeader>();
                    int used = 3;
                    if (getHeaders is not null)
                    {
                        headers.AddRange(getHeaders);
                        foreach (var h in getHeaders) used += h.EncodedSize;
                        getHeaders = null;
                    }

                    int room = Math.Max(1, _peerMaxPacket - used - 3);
                    int chunk = Math.Min(room, getBody.Length - getOffset);
                    bool last = getOffset + chunk >= getBody.Length;
                    headers.Add(new ObexHeader(last ? ObexHeader.EndOfBody : ObexHeader.Body, getBody.AsSpan(getOffset, chunk).ToArray()));
                    getOffset += chunk;
                    if (last) getBody = null;

                    await SendAsync(last ? ObexStatus.Success : ObexStatus.Continue, headers, ct);
                    break;
                }

                default:
                    await SendAsync(ObexStatus.NotImplemented, null, ct);
                    break;
            }
        }
    }

    private async Task SendAsync(byte code, List<ObexHeader>? headers, CancellationToken ct)
    {
        var packet = new List<byte> { code, 0, 0 };
        if (headers is not null)
            foreach (var h in headers) h.WriteTo(packet);
        ObexCodec.Seal(packet);
        await _transport.WriteAsync(packet.ToArray(), ct);
    }
}
