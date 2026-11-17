using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;

namespace Native.Protocol;

// Message Access Profile (MAP): o perfil Bluetooth que kits de carro usam para ler e
// responder SMS/iMessage. O PC é o cliente (MCE) e o iPhone, o servidor (MSE).

public static class MapConstants
{
    /// <summary>Serviço de acesso às mensagens no iPhone (MAS).</summary>
    public static readonly Guid MasService = Guid.Parse("00001132-0000-1000-8000-00805F9B34FB");

    /// <summary>Serviço de avisos, que o PC publica para o iPhone conectar de volta (MNS).</summary>
    public static readonly Guid MnsService = Guid.Parse("00001133-0000-1000-8000-00805F9B34FB");

    public static readonly byte[] MasTarget =
        { 0xBB, 0x58, 0x2B, 0x40, 0x42, 0x0C, 0x11, 0xDB, 0xB0, 0xDE, 0x08, 0x00, 0x20, 0x0C, 0x9A, 0x66 };

    public static readonly byte[] MnsTarget =
        { 0xBB, 0x58, 0x2B, 0x41, 0x42, 0x0C, 0x11, 0xDB, 0xB0, 0xDE, 0x08, 0x00, 0x20, 0x0C, 0x9A, 0x66 };

    public const string TypeMessage = "x-bt/message";
    public const string TypeListing = "x-bt/MAP-msg-listing";
    public const string TypeEventReport = "x-bt/MAP-event-report";
    public const string TypeNotificationRegistration = "x-bt/MAP-NotificationRegistration";

    // Tags dos parâmetros de aplicação
    public const byte TagMaxListCount = 0x01, TagAttachment = 0x0A, TagNotificationStatus = 0x0E, TagCharset = 0x14;
}

public sealed record BMessage(
    string Type, string Folder, string Status,
    string SenderName, string SenderAddress,
    string RecipientName, string RecipientAddress,
    string Text);

public static class BMessageCodec
{
    /// <summary>Monta a mensagem a enviar. O iPhone decide sozinho entre iMessage e SMS.</summary>
    public static string Build(string recipientName, string recipientAddress, string text)
    {
        text = text.Replace("\r\n", "\n").Replace('\r', '\n').Replace("\n", "\r\n");
        var message = $"BEGIN:MSG\r\n{text}\r\nEND:MSG\r\n";
        var contact = TextMatch.IsEmail(recipientAddress) ? $"EMAIL:{recipientAddress}" : $"TEL:{recipientAddress}";

        return "BEGIN:BMSG\r\n" +
               "VERSION:1.0\r\n" +
               "STATUS:READ\r\n" +
               "TYPE:SMS_GSM\r\n" +
               "FOLDER:telecom/msg/outbox\r\n" +
               "BEGIN:BENV\r\n" +
               "BEGIN:VCARD\r\n" +
               "VERSION:2.1\r\n" +
               $"N:{recipientName.Replace("\r", " ").Replace("\n", " ")}\r\n" +
               $"{contact}\r\n" +
               "END:VCARD\r\n" +
               "BEGIN:BBODY\r\n" +
               "CHARSET:UTF-8\r\n" +
               $"LENGTH:{Encoding.UTF8.GetByteCount(message)}\r\n" +
               message +
               "END:BBODY\r\n" +
               "END:BENV\r\n" +
               "END:BMSG\r\n";
    }

    public static BMessage Parse(string raw)
    {
        int bodyStart = raw.IndexOf("BEGIN:BBODY", StringComparison.OrdinalIgnoreCase);
        var header = bodyStart >= 0 ? raw[..bodyStart] : raw;
        int envelope = header.IndexOf("BEGIN:BENV", StringComparison.OrdinalIgnoreCase);
        var originatorPart = envelope >= 0 ? header[..envelope] : header;
        var recipientPart = envelope >= 0 ? header[envelope..] : "";

        var sender = VCardParser.Parse(originatorPart).FirstOrDefault();
        var recipient = VCardParser.Parse(recipientPart).FirstOrDefault();

        int firstCard = originatorPart.IndexOf("BEGIN:VCARD", StringComparison.OrdinalIgnoreCase);
        var properties = firstCard >= 0 ? originatorPart[..firstCard] : originatorPart;

        return new BMessage(
            Field(properties, "TYPE"), Field(properties, "FOLDER"), Field(properties, "STATUS"),
            sender?.DisplayName ?? "", sender?.PrimaryAddress ?? "",
            recipient?.DisplayName ?? "", recipient?.PrimaryAddress ?? "",
            ExtractText(raw));
    }

    private static string Field(string block, string key)
    {
        foreach (var line in block.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith(key + ":", StringComparison.OrdinalIgnoreCase)) return trimmed[(key.Length + 1)..].Trim();
        }
        return "";
    }

    private static string ExtractText(string raw)
    {
        int begin = raw.IndexOf("BEGIN:MSG", StringComparison.OrdinalIgnoreCase);
        if (begin < 0) return "";
        int start = raw.IndexOf('\n', begin);
        if (start < 0) return "";
        start++;
        int end = raw.LastIndexOf("END:MSG", StringComparison.OrdinalIgnoreCase);
        var text = end >= start ? raw[start..end] : raw[start..];
        return text.TrimEnd('\r', '\n').Replace("\r\n", "\n");
    }
}

public sealed record MapListedMessage(
    string Handle, string Subject, DateTime When,
    string SenderName, string SenderAddress, string RecipientAddress,
    string Type, bool Read, bool Sent);

public sealed record MapEvent(string Type, string Handle, string Folder, string MessageType);

public static class MapXml
{
    public static List<MapListedMessage> ParseListing(string xml)
    {
        var list = new List<MapListedMessage>();
        ReadElements(xml, "msg", get => list.Add(new MapListedMessage(
            get("handle"), get("subject"),
            TimeFormat.ParseCompact(get("datetime")) ?? DateTime.MinValue,
            get("sender_name"), get("sender_addressing"), get("recipient_addressing"),
            get("type"),
            get("read").Equals("yes", StringComparison.OrdinalIgnoreCase),
            get("sent").Equals("yes", StringComparison.OrdinalIgnoreCase))));
        return list;
    }

    public static List<MapEvent> ParseEventReport(string xml)
    {
        var list = new List<MapEvent>();
        ReadElements(xml, "event", get => list.Add(new MapEvent(get("type"), get("handle"), get("folder"), get("msg_type"))));
        return list;
    }

    private static void ReadElements(string xml, string element, Action<Func<string, string>> onElement)
    {
        xml = xml.Trim('\0', '﻿', ' ', '\r', '\n', '\t');
        if (xml.Length == 0) return;

        // Os celulares mandam DOCTYPE e, às vezes, caracteres inválidos: leitura tolerante.
        var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, XmlResolver = null, CheckCharacters = false };
        try
        {
            using var reader = XmlReader.Create(new StringReader(xml), settings);
            while (reader.Read())
            {
                if (reader.NodeType != XmlNodeType.Element || reader.Name != element) continue;
                var current = reader;
                onElement(name => current.GetAttribute(name) ?? "");
            }
        }
        catch (XmlException)
        {
            // Fica com o que deu para ler até o ponto do erro.
        }
    }
}

public static class MapMatch
{
    /// <summary>
    /// Acha, na caixa de entrada, a mensagem cujo remetente bate com o título de uma notificação
    /// (nome do contato, número ou e-mail).
    /// </summary>
    public static MapListedMessage? FindSender(IEnumerable<MapListedMessage> messages, string title)
    {
        var ordered = messages.Where(m => m.SenderAddress.Length > 0).OrderByDescending(m => m.When).ToList();
        var wanted = TextMatch.Normalize(title);
        if (wanted.Length == 0) return null;

        var exact = ordered.FirstOrDefault(m => TextMatch.Normalize(m.SenderName) == wanted);
        if (exact is not null) return exact;

        if (TextMatch.LooksLikePhone(title) || TextMatch.IsEmail(title))
            return ordered.FirstOrDefault(m => TextMatch.SameAddress(m.SenderAddress, title));

        return ordered.FirstOrDefault(m =>
        {
            var name = TextMatch.Normalize(m.SenderName);
            return name.Length >= 3 && (name.Contains(wanted) || wanted.Contains(name));
        });
    }
}

/// <summary>Sessão MAP do lado do PC, em cima de um <see cref="ObexClient"/>.</summary>
public sealed class MapClient
{
    private readonly ObexClient _obex;

    public MapClient(ObexClient obex)
    {
        _obex = obex;
    }

    public async Task ConnectAsync(CancellationToken ct = default)
    {
        await _obex.ConnectAsync(MapConstants.MasTarget, ct);
        try { await _obex.SetPathAsync("", false, ct); }
        catch (ObexException) { /* já estamos na raiz */ }
        await _obex.SetPathAsync("telecom", false, ct);
        await _obex.SetPathAsync("msg", false, ct);
    }

    /// <summary>Pede ao iPhone para avisar (pelo canal MNS) quando chegar mensagem nova.</summary>
    public Task SetNotificationRegistrationAsync(bool enabled, CancellationToken ct = default) =>
        _obex.PutAsync(new[]
        {
            ObexHeader.Ascii(ObexHeader.Type, MapConstants.TypeNotificationRegistration),
            new ObexHeader(ObexHeader.AppParameters, ObexAppParams.Build(
                (MapConstants.TagNotificationStatus, ObexAppParams.U8(enabled ? (byte)1 : (byte)0)))),
        }, new byte[] { 0x30 }, ct);

    public async Task<List<MapListedMessage>> GetMessagesListingAsync(string folder, int max, CancellationToken ct = default)
    {
        var result = await _obex.GetAsync(new[]
        {
            ObexHeader.Text(ObexHeader.Name, folder),
            ObexHeader.Ascii(ObexHeader.Type, MapConstants.TypeListing),
            new ObexHeader(ObexHeader.AppParameters, ObexAppParams.Build(
                (MapConstants.TagMaxListCount, ObexAppParams.U16(max)))),
        }, ct);
        return MapXml.ParseListing(Encoding.UTF8.GetString(result.Body));
    }

    public async Task<BMessage> GetMessageAsync(string handle, CancellationToken ct = default)
    {
        var result = await _obex.GetAsync(new[]
        {
            ObexHeader.Text(ObexHeader.Name, handle),
            ObexHeader.Ascii(ObexHeader.Type, MapConstants.TypeMessage),
            new ObexHeader(ObexHeader.AppParameters, ObexAppParams.Build(
                (MapConstants.TagAttachment, ObexAppParams.U8(0)),
                (MapConstants.TagCharset, ObexAppParams.U8(1)))),
        }, ct);
        return BMessageCodec.Parse(Encoding.UTF8.GetString(result.Body));
    }

    /// <summary>Envia uma mensagem. Devolve o identificador que o iPhone deu a ela, se informado.</summary>
    public async Task<string?> PushMessageAsync(string recipientName, string recipientAddress, string text, CancellationToken ct = default)
    {
        var body = Encoding.UTF8.GetBytes(BMessageCodec.Build(recipientName, recipientAddress, text));
        var result = await _obex.PutAsync(new[]
        {
            ObexHeader.Text(ObexHeader.Name, "outbox"),
            ObexHeader.Ascii(ObexHeader.Type, MapConstants.TypeMessage),
            new ObexHeader(ObexHeader.AppParameters, ObexAppParams.Build(
                (MapConstants.TagCharset, ObexAppParams.U8(1)))),
        }, body, ct);
        return result.Find(ObexHeader.Name)?.AsText();
    }
}
