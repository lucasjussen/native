using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Native.Protocol.Share;

/// <summary>
/// Responde no mDNS (Bonjour) por um nome ".local", para o Atalho do iPhone achar o PC
/// mesmo quando o roteador troca o IP dele.
/// </summary>
public static class MdnsPacket
{
    public const ushort TypeA = 1, TypeAny = 255;

    public sealed record Question(string Name, ushort Type, bool UnicastResponse);

    /// <summary>Lê as perguntas de um pacote mDNS. Respostas e pacotes estranhos são ignorados.</summary>
    public static (ushort Id, List<Question> Questions) ParseQuery(byte[] packet)
    {
        var questions = new List<Question>();
        if (packet.Length < 12) return (0, questions);
        ushort id = (ushort)(packet[0] << 8 | packet[1]);
        bool isResponse = (packet[2] & 0x80) != 0;
        int qd = packet[4] << 8 | packet[5];
        if (isResponse || qd == 0 || qd > 64) return (id, questions);

        int pos = 12;
        for (int i = 0; i < qd; i++)
        {
            var name = ReadName(packet, ref pos);
            if (name is null || pos + 4 > packet.Length) break;
            ushort type = (ushort)(packet[pos] << 8 | packet[pos + 1]);
            ushort cls = (ushort)(packet[pos + 2] << 8 | packet[pos + 3]);
            pos += 4;
            questions.Add(new Question(name, type, (cls & 0x8000) != 0));
        }
        return (id, questions);
    }

    private static string? ReadName(byte[] p, ref int pos)
    {
        var labels = new List<string>();
        int cursor = pos, jumps = 0;
        bool jumped = false;
        while (true)
        {
            if (cursor >= p.Length) return null;
            int len = p[cursor];
            if (len == 0)
            {
                cursor++;
                break;
            }
            if ((len & 0xC0) == 0xC0)
            {
                if (cursor + 1 >= p.Length || ++jumps > 16) return null;
                int target = (len & 0x3F) << 8 | p[cursor + 1];
                if (!jumped) pos = cursor + 2;
                jumped = true;
                cursor = target;
                continue;
            }
            if (cursor + 1 + len > p.Length) return null;
            labels.Add(Encoding.UTF8.GetString(p, cursor + 1, len));
            cursor += 1 + len;
        }
        if (!jumped) pos = cursor;
        return string.Join('.', labels);
    }

    /// <summary>Monta a resposta "nome.local tem o IP x" (registro A, com cache-flush).</summary>
    public static byte[] BuildAnswer(ushort id, string name, IPAddress address, uint ttl = 120)
    {
        var b = new List<byte>
        {
            (byte)(id >> 8), (byte)id,
            0x84, 0x00,        // resposta autoritativa
            0, 0,              // perguntas
            0, 1,              // respostas
            0, 0, 0, 0,
        };
        foreach (var label in name.TrimEnd('.').Split('.'))
        {
            var bytes = Encoding.UTF8.GetBytes(label);
            b.Add((byte)bytes.Length);
            b.AddRange(bytes);
        }
        b.Add(0);
        b.AddRange(new byte[] { 0, 1, 0x80, 1 });   // tipo A, classe IN + cache flush
        b.AddRange(new[] { (byte)(ttl >> 24), (byte)(ttl >> 16), (byte)(ttl >> 8), (byte)ttl });
        b.AddRange(new byte[] { 0, 4 });
        b.AddRange(address.GetAddressBytes());
        return b.ToArray();
    }

    /// <summary>"LUCAS-PC" → "native-lucas-pc.local".</summary>
    public static string HostNameFor(string computerName)
    {
        var sb = new StringBuilder();
        foreach (var ch in computerName.ToLowerInvariant())
            sb.Append(ch is >= 'a' and <= 'z' or >= '0' and <= '9' ? ch : '-');
        var clean = sb.ToString().Trim('-');
        while (clean.Contains("--")) clean = clean.Replace("--", "-");
        return $"native-{(clean.Length > 0 ? clean : "pc")}.local";
    }
}

/// <summary>Respondedor mDNS mínimo: só responde pelo próprio nome.</summary>
public sealed class MdnsResponder : IDisposable
{
    private static readonly IPAddress Group = IPAddress.Parse("224.0.0.251");
    private const int Port = 5353;

    private readonly string _name;
    private readonly Func<IPAddress?> _address;
    private UdpClient? _udp;
    private CancellationTokenSource? _cts;

    public bool IsRunning => _udp is not null;
    public string Name => _name;

    public MdnsResponder(string name, Func<IPAddress?> address)
    {
        _name = name;
        _address = address;
    }

    public void Start()
    {
        if (_udp is not null) return;
        var udp = new UdpClient(AddressFamily.InterNetwork);
        udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        udp.Client.Bind(new IPEndPoint(IPAddress.Any, Port));
        foreach (var local in Network.LanAddresses())
        {
            try { udp.JoinMulticastGroup(Group, local); } catch (SocketException) { }
        }
        try { udp.JoinMulticastGroup(Group); } catch (SocketException) { }
        udp.MulticastLoopback = false;

        _udp = udp;
        _cts = new CancellationTokenSource();
        _ = Task.Run(() => LoopAsync(udp, _cts.Token));
    }

    private async Task LoopAsync(UdpClient udp, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            UdpReceiveResult r;
            try { r = await udp.ReceiveAsync(ct); }
            catch { return; }

            try
            {
                var (id, questions) = MdnsPacket.ParseQuery(r.Buffer);
                foreach (var q in questions)
                {
                    if (!string.Equals(q.Name, _name, StringComparison.OrdinalIgnoreCase)) continue;
                    if (q.Type is not (MdnsPacket.TypeA or MdnsPacket.TypeAny)) continue;
                    var ip = _address();
                    if (ip is null) continue;

                    bool legacy = r.RemoteEndPoint.Port != Port;   // consulta "comum" de DNS: responde direto
                    var answer = MdnsPacket.BuildAnswer(legacy ? id : (ushort)0, _name, ip, legacy ? 10u : 120u);
                    var to = legacy || q.UnicastResponse ? r.RemoteEndPoint : new IPEndPoint(Group, Port);
                    await udp.SendAsync(answer, answer.Length, to);
                    break;
                }
            }
            catch
            {
                // pacote malformado ou rede caiu: ignora e segue
            }
        }
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _udp?.Dispose();
        _udp = null;
    }
}
