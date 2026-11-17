using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Native.Protocol;

namespace Native.Tests;

public static class TextTests
{
    public static IEnumerable<TestCase> All()
    {
        yield return new TestCase("código: SMS de banco com 6 dígitos", () =>
            Check.Equal("482913", VerificationCode.Find("Nubank", "", "Seu código de verificação é 482913. Não compartilhe.")));

        yield return new TestCase("código: formato 123 456 e 123-456", () =>
        {
            Check.Equal("123456", VerificationCode.Find("Google", "", "G-123 456 é seu código de verificação"));
            Check.Equal("987654", VerificationCode.Find("", "", "Use o código 987-654 para entrar"));
        });

        yield return new TestCase("código: prefere 6 dígitos quando há vários números", () =>
            Check.Equal("551200", VerificationCode.Find("", "", "Código 551200 válido até 2026 para o pedido 4471")));

        yield return new TestCase("código: ignora valores, datas e horas", () =>
        {
            Check.Null(VerificationCode.Find("Banco", "", "Compra de R$ 1500 aprovada. Confirma? Ligue já."), "valor em reais");
            Check.Null(VerificationCode.Find("Agenda", "", "Confirmação: consulta em 16/11/2026 às 14:30"), "data e hora");
            Check.Null(VerificationCode.Find("Loja", "", "Seu pedido 12345 saiu para entrega"), "sem palavra-chave");
        });

        yield return new TestCase("nomes: ignora acentos, caixa e espaços", () =>
        {
            Check.True(TextMatch.SameName("João  da Silva", "joao da silva"), "João");
            Check.True(!TextMatch.SameName("", ""), "vazios não casam");
            Check.Equal("JS", TextMatch.Initials("João da Silva"));
            Check.Equal("M", TextMatch.Initials("Mãe"));
        });

        yield return new TestCase("telefones: mesmo número com e sem +55, DDD e traços", () =>
        {
            Check.True(TextMatch.SameAddress("+55 (41) 99876-5432", "041998765432"), "com DDI");
            Check.True(TextMatch.SameAddress("99876-5432", "+5541998765432"), "sem DDD");
            Check.True(!TextMatch.SameAddress("+5541998765432", "+5541998765433"), "números diferentes");
            Check.True(TextMatch.SameAddress("Lucas@Exemplo.com", "lucas@exemplo.com"), "e-mail");
            Check.True(!TextMatch.SameAddress("190", "193"), "números curtos");
            Check.Equal("98765432", TextMatch.AddressKey("+55 41 99876-5432"));
        });

        yield return new TestCase("tempo: relativo e relógio", () =>
        {
            var now = new DateTime(2026, 11, 17, 15, 0, 0);
            Check.Equal("agora", TimeFormat.Relative(now.AddSeconds(-20), now));
            Check.Equal("há 5 min", TimeFormat.Relative(now.AddMinutes(-5), now));
            Check.Equal("09:30", TimeFormat.Relative(new DateTime(2026, 11, 17, 9, 30, 0), now));
            Check.Equal("Ontem", TimeFormat.Relative(now.AddDays(-1), now));
            Check.Equal("10/11", TimeFormat.Relative(new DateTime(2026, 11, 10, 8, 0, 0), now));
            Check.Equal("3:07", TimeFormat.Clock(187));
            Check.Equal("1:02:03", TimeFormat.Clock(3723));
            Check.Equal(new DateTime(2026, 11, 16, 10, 10, 10), TimeFormat.ParseCompact("20261116T101010")!.Value);
        });
    }
}

public static class AncsTests
{
    public static IEnumerable<TestCase> All()
    {
        yield return new TestCase("ANCS: evento de notificação", () =>
        {
            // adicionada, flags = importante + ação negativa, categoria Social, 3 na categoria, uid 0x01020304
            var ok = AncsEvent.TryParse(new byte[] { 0, 0x12, 4, 3, 0x04, 0x03, 0x02, 0x01 }, out var e);
            Check.True(ok, "parse");
            Check.Equal(AncsEventKind.Added, e.Kind);
            Check.Equal(AncsCategory.Social, e.Category);
            Check.Equal(0x01020304u, e.Uid);
            Check.True(e.IsImportant && e.HasNegativeAction && !e.HasPositiveAction && !e.IsPreExisting, "flags");
            Check.True(!AncsEvent.TryParse(new byte[] { 0, 0, 0 }, out _), "curto demais");
        });

        yield return new TestCase("ANCS: comandos", () =>
        {
            Check.Bytes(new byte[] { 0, 0x78, 0x56, 0x34, 0x12, 0, 1, 96, 0, 3, 0x58, 0x02, 5 },
                AncsCommands.GetNotificationAttributes(0x12345678, (0, 0), (1, 96), (3, 600), (5, 0)));
            Check.Bytes(new byte[] { 2, 1, 0, 0, 0, 1 }, AncsCommands.PerformAction(1, positive: false));
            Check.Bytes(new byte[] { 1, (byte)'a', (byte)'.', (byte)'b', 0, 0 }, AncsCommands.GetAppAttributes("a.b", 0));
        });

        yield return new TestCase("ANCS: resposta fatiada em pacotes pequenos", () =>
        {
            var response = BuildNotificationResponse(7, ("net.whatsapp.WhatsApp", 0), ("Mãe 💙", 1), ("", 2), ("Chegou bem? Avisa quando puder.", 3));
            var assembler = new AncsResponseAssembler();
            assembler.ExpectNotification(7, 4);

            Dictionary<byte, string>? result = null;
            for (int i = 0; i < response.Length; i += 9)
            {
                bool done = assembler.TryFeed(response.AsSpan(i, Math.Min(9, response.Length - i)).ToArray(), out var attrs);
                if (done) result = attrs;
                else Check.True(i + 9 < response.Length, "só termina no último pedaço");
            }

            Check.True(result is not null, "resposta completa");
            Check.Equal("net.whatsapp.WhatsApp", result![0]);
            Check.Equal("Mãe 💙", result[1]);
            Check.Equal("", result[2]);
            Check.Equal("Chegou bem? Avisa quando puder.", result[3]);
        });

        yield return new TestCase("ANCS: descarta resto de resposta antiga", () =>
        {
            var assembler = new AncsResponseAssembler();
            assembler.ExpectNotification(2, 1);
            Check.True(!assembler.TryFeed(BuildNotificationResponse(1, ("velho", 1)), out _), "uid errado");
            Check.True(assembler.TryFeed(BuildNotificationResponse(2, ("novo", 1)), out var attrs), "uid certo");
            Check.Equal("novo", attrs[1]);
            Check.True(!assembler.TryFeed(new byte[] { 0, 2, 0, 0, 0 }, out _), "nada pendente");
        });

        yield return new TestCase("ANCS: nome do app", () =>
        {
            var response = new List<byte> { 1 };
            response.AddRange(Encoding.UTF8.GetBytes("com.burbn.instagram"));
            response.Add(0);
            var name = Encoding.UTF8.GetBytes("Instagram");
            response.Add(0);
            response.Add((byte)name.Length);
            response.Add(0);
            response.AddRange(name);

            var assembler = new AncsResponseAssembler();
            assembler.ExpectApp(1);
            var bytes = response.ToArray();
            Check.True(!assembler.TryFeed(bytes[..10], out _), "metade");
            Check.True(assembler.TryFeed(bytes[10..], out var attrs), "inteira");
            Check.Equal("Instagram", attrs[0]);
        });

        yield return new TestCase("AMS: tocando agora", () =>
        {
            var tracker = new NowPlayingTracker();
            var t0 = new DateTime(2026, 11, 17, 12, 0, 0);
            tracker.Apply(Update(0, 0, "Spotify"), t0);
            tracker.Apply(Update(2, 2, "Aquarela"), t0);
            tracker.Apply(Update(2, 0, "Toquinho"), t0);
            tracker.Apply(Update(2, 3, "253.000"), t0);
            var np = tracker.Apply(Update(0, 1, "1,1.0,30.500"), t0)!;

            Check.Equal("Spotify", np.Player);
            Check.Equal("Aquarela", np.Title);
            Check.Equal("Toquinho", np.Artist);
            Check.True(np.IsPlaying, "tocando");
            Check.Equal(253.0, np.Duration);
            Check.Equal(40.5, np.ElapsedAt(t0.AddSeconds(10)), "avança enquanto toca");

            var paused = tracker.Apply(Update(0, 1, "0,0.0,45.000"), t0.AddSeconds(15))!;
            Check.True(!paused.IsPlaying, "pausado");
            Check.Equal(45.0, paused.ElapsedAt(t0.AddMinutes(5)), "parado não avança");
        });
    }

    internal static IEnumerable<TestCase> Media()
    {
        yield return new TestCase("AMS: volume do iPhone", () =>
        {
            var tracker = new NowPlayingTracker();
            var t0 = DateTime.Now;
            tracker.Apply(Update(2, 2, "Tudo Vai Dar Certo"), t0);
            var np = tracker.Apply(Update(0, 2, "0.4375"), t0)!;
            Check.True(np.HasVolume, "tem volume");
            Check.Equal(0.4375, np.Volume);
            Check.True(!new NowPlayingTracker().Apply(Update(2, 2, "x"), t0)!.HasVolume, "sem volume ainda");
        });

        yield return new TestCase("AMS: comandos aceitos pelo app de música", () =>
        {
            var set = MediaCommands.ParseSupported(new byte[] { 0, 1, 2, 3, 4, 5, 6, 9, 10, 200 });
            Check.True(set.Contains(MediaCommand.SkipForward) && set.Contains(MediaCommand.SkipBackward), "pulos");
            Check.True(!set.Contains(MediaCommand.LikeTrack), "sem curtir");
            Check.Equal(9, set.Count, "ignora código desconhecido");
        });

        yield return new TestCase("AMS: degraus de volume e pulos na música", () =>
        {
            Check.Equal(4, MediaCommands.VolumeStepsBetween(0.25, 0.5));
            Check.Equal(-16, MediaCommands.VolumeStepsBetween(1, 0));
            Check.Equal(0, MediaCommands.VolumeStepsBetween(0.5, 0.52));
            Check.Equal(0, MediaCommands.VolumeStepsBetween(1, 1.5), "não passa de 100%");
            Check.Equal(4, MediaCommands.SkipStepsBetween(30, 90));
            Check.Equal(-2, MediaCommands.SkipStepsBetween(100, 70));
            Check.Equal(40, MediaCommands.SkipStepsBetween(0, 5000), "limite de pulos");
        });
    }

    private static byte[] Update(byte entity, byte attribute, string value)
    {
        var bytes = new List<byte> { entity, attribute, 0 };
        bytes.AddRange(Encoding.UTF8.GetBytes(value));
        return bytes.ToArray();
    }

    private static byte[] BuildNotificationResponse(uint uid, params (string Value, byte Id)[] attributes)
    {
        var bytes = new List<byte> { 0 };
        bytes.AddRange(BitConverter.GetBytes(uid));
        foreach (var (value, id) in attributes)
        {
            var encoded = Encoding.UTF8.GetBytes(value);
            bytes.Add(id);
            bytes.Add((byte)encoded.Length);
            bytes.Add((byte)(encoded.Length >> 8));
            bytes.AddRange(encoded);
        }
        return bytes.ToArray();
    }
}

public static class ObexTests
{
    public static IEnumerable<TestCase> All()
    {
        yield return new TestCase("OBEX: cabeçalhos vão e voltam", () =>
        {
            var packet = new List<byte> { 0, 0, 0 };
            ObexHeader.UInt32(ObexHeader.ConnectionId, 0xDEADBEEF).WriteTo(packet);
            ObexHeader.Text(ObexHeader.Name, "ação").WriteTo(packet);
            ObexHeader.Text(ObexHeader.Name, "").WriteTo(packet);
            ObexHeader.Ascii(ObexHeader.Type, "x-bt/message").WriteTo(packet);
            new ObexHeader(ObexHeader.EndOfBody, new byte[] { 1, 2, 3 }).WriteTo(packet);
            ObexCodec.Seal(packet);

            Check.Equal(packet.Count, (packet[1] << 8) | packet[2], "tamanho no cabeçalho");
            var headers = ObexCodec.ParseHeaders(packet.ToArray(), 3);
            Check.Equal(5, headers.Count);
            Check.Equal(0xDEADBEEFu, headers[0].AsUInt32());
            Check.Equal("ação", headers[1].AsText());
            Check.Equal(0, headers[2].Value.Length, "nome vazio");
            Check.Equal("x-bt/message", headers[3].AsAscii());
            Check.Bytes(new byte[] { 1, 2, 3 }, headers[4].Value);
        });

        yield return new TestCase("OBEX: parâmetros de aplicação", () =>
        {
            var built = ObexAppParams.Build((0x01, ObexAppParams.U16(40)), (0x14, ObexAppParams.U8(1)));
            Check.Bytes(new byte[] { 0x01, 2, 0, 40, 0x14, 1, 1 }, built);
            var parsed = ObexAppParams.Parse(built);
            Check.Equal(40, ObexAppParams.ReadU16(parsed[0x01]));
            Check.Bytes(new byte[] { 0, 0, 0, 0, 0, 0, 0x01, 0x87 }, ObexAppParams.U64(0x187));
        });

        foreach (int fragment in new[] { 0, 5 })
        {
            yield return new TestCase($"OBEX: cliente e servidor, corpo grande nos dois sentidos (pedaços de {fragment})", async () =>
            {
                var (a, b) = PipeEnd.CreatePair(fragment);
                var big = Enumerable.Range(0, 40_000).Select(i => (byte)(i * 7)).ToArray();
                byte[]? received = null;
                string? path = null;

                var server = new ObexServerSession(b, new byte[] { 9, 9 }, request =>
                {
                    switch (request.Opcode)
                    {
                        case ObexOp.SetPath:
                            path = request.Name;
                            return Task.FromResult(request.Name == "proibida" ? ObexReply.Fail(ObexStatus.Forbidden) : ObexReply.Ok());
                        case ObexOp.Put:
                            received = request.Body;
                            var ok = ObexReply.Ok();
                            ok.Headers.Add(ObexHeader.Text(ObexHeader.Name, "recibo"));
                            return Task.FromResult(ok);
                        default:
                            return Task.FromResult(request.Name == "grande" ? new ObexReply { Body = big } : ObexReply.Fail(ObexStatus.NotFound));
                    }
                }, maxPacket: 1024);
                var serverTask = server.RunAsync(CancellationToken.None);

                using var client = new ObexClient(a, TimeSpan.FromSeconds(5));
                await client.ConnectAsync(new byte[] { 9, 9 });
                Check.Equal(1024, client.MaxPacketSize, "tamanho negociado");

                await client.SetPathAsync("telecom");
                Check.Equal("telecom", path);
                await Check.ThrowsAsync<ObexException>(() => client.SetPathAsync("proibida"), "pasta proibida");

                var got = await client.GetAsync(new[] { ObexHeader.Text(ObexHeader.Name, "grande") });
                Check.Bytes(big, got.Body, "GET grande");
                await Check.ThrowsAsync<ObexException>(() => client.GetAsync(new[] { ObexHeader.Text(ObexHeader.Name, "nada") }), "GET inexistente");

                var put = await client.PutAsync(new[] { ObexHeader.Ascii(ObexHeader.Type, "teste") }, big);
                Check.Bytes(big, received!, "PUT grande");
                Check.Equal("recibo", put.Find(ObexHeader.Name)?.AsText());

                await client.PutAsync(new[] { ObexHeader.Ascii(ObexHeader.Type, "teste") }, Array.Empty<byte>());
                Check.Equal(0, received!.Length, "PUT vazio");

                await client.DisconnectAsync();
                await serverTask;
            });
        }

        yield return new TestCase("OBEX: sem resposta vira erro de tempo esgotado", async () =>
        {
            var (a, _) = PipeEnd.CreatePair();
            using var client = new ObexClient(a, TimeSpan.FromMilliseconds(200));
            await Check.ThrowsAsync<TimeoutException>(() => client.ConnectAsync(null), "ninguém do outro lado");
        });
    }
}

public static class MapTests
{
    private const string Listing =
        "<?xml version=\"1.0\"?>\n<!DOCTYPE MAP-msg-listing SYSTEM \"MAP-msg-listing.dtd\">\n" +
        "<MAP-msg-listing version=\"1.0\">\n" +
        "  <msg handle=\"0A01\" subject=\"Oi, tudo bem?\" datetime=\"20261116T213000\" sender_name=\"Amanda\" sender_addressing=\"+5541998765432\" recipient_addressing=\"\" type=\"SMS_GSM\" size=\"13\" read=\"no\" sent=\"no\"/>\n" +
        "  <msg handle=\"0A02\" subject=\"Seu c&#243;digo &#233; 1234\" datetime=\"20261116T220000\" sender_name=\"\" sender_addressing=\"29000\" type=\"SMS_GSM\" read=\"yes\" sent=\"no\"/>\n" +
        "  <msg handle=\"0A03\" subject=\"Boa noite\" datetime=\"20261116T221500\" sender_name=\"João Pedro\" sender_addressing=\"jp@exemplo.com\" type=\"SMS_GSM\" read=\"yes\" sent=\"no\"/>\n" +
        "</MAP-msg-listing>";

    public static IEnumerable<TestCase> All()
    {
        yield return new TestCase("MAP: lista de mensagens com DOCTYPE e acentos", () =>
        {
            var list = MapXml.ParseListing(Listing);
            Check.Equal(3, list.Count);
            Check.Equal("Amanda", list[0].SenderName);
            Check.Equal("+5541998765432", list[0].SenderAddress);
            Check.Equal(new DateTime(2026, 11, 16, 21, 30, 0), list[0].When);
            Check.True(!list[0].Read && list[1].Read, "lida");
            Check.Equal("Seu código é 1234", list[1].Subject);
            Check.Equal(0, MapXml.ParseListing("").Count, "vazio");
            Check.Equal(1, MapXml.ParseListing("<MAP-msg-listing><msg handle=\"1\" sender_addressing=\"5\"/><quebrado").Count, "XML truncado");
        });

        yield return new TestCase("MAP: achar o remetente pelo título da notificação", () =>
        {
            var list = MapXml.ParseListing(Listing);
            Check.Equal("+5541998765432", MapMatch.FindSender(list, "amanda")?.SenderAddress);
            Check.Equal("jp@exemplo.com", MapMatch.FindSender(list, "Joao Pedro")?.SenderAddress);
            Check.Equal("29000", MapMatch.FindSender(list, "29000")?.SenderAddress);
            Check.Null(MapMatch.FindSender(list, "Desconhecido"), "ninguém");
        });

        yield return new TestCase("MAP: aviso de mensagem nova", () =>
        {
            var events = MapXml.ParseEventReport(
                "<MAP-event-report version=\"1.0\"><event type=\"NewMessage\" handle=\"12AB\" folder=\"TELECOM/MSG/INBOX\" msg_type=\"SMS_GSM\"/></MAP-event-report>");
            Check.Equal(1, events.Count);
            Check.Equal("NewMessage", events[0].Type);
            Check.Equal("12AB", events[0].Handle);
        });

        yield return new TestCase("MAP: mensagem montada e lida de volta", () =>
        {
            var raw = BMessageCodec.Build("Amanda", "+5541998765432", "Oi!\nChego às 19h 🙂");
            Check.True(raw.Contains("TEL:+5541998765432\r\n"), "telefone");
            Check.True(raw.Contains("FOLDER:telecom/msg/outbox\r\n"), "pasta");
            int declared = int.Parse(raw.Split("LENGTH:")[1].Split("\r\n")[0]);
            int start = raw.IndexOf("BEGIN:MSG", StringComparison.Ordinal);
            int end = raw.IndexOf("END:MSG\r\n", StringComparison.Ordinal) + "END:MSG\r\n".Length;
            Check.Equal(Encoding.UTF8.GetByteCount(raw[start..end]), declared, "LENGTH em bytes UTF-8");

            var parsed = BMessageCodec.Parse(raw);
            Check.Equal("Oi!\nChego às 19h 🙂", parsed.Text);
            Check.Equal("+5541998765432", parsed.RecipientAddress);
            Check.Equal("SMS_GSM", parsed.Type);
            Check.True(BMessageCodec.Build("X", "x@y.com", "a").Contains("EMAIL:x@y.com"), "e-mail do iMessage");
        });

        yield return new TestCase("MAP: mensagem recebida do iPhone", () =>
        {
            var raw = "BEGIN:BMSG\r\nVERSION:1.0\r\nSTATUS:UNREAD\r\nTYPE:SMS_GSM\r\nFOLDER:TELECOM/MSG/INBOX\r\n" +
                      "BEGIN:VCARD\r\nVERSION:2.1\r\nN:Silva;Amanda\r\nTEL:+5541998765432\r\nEND:VCARD\r\n" +
                      "BEGIN:BENV\r\nBEGIN:BBODY\r\nCHARSET:UTF-8\r\nLENGTH:38\r\nBEGIN:MSG\r\nJá saí. Te ligo.\r\nEND:MSG\r\nEND:BBODY\r\nEND:BENV\r\nEND:BMSG\r\n";
            var parsed = BMessageCodec.Parse(raw);
            Check.Equal("Amanda Silva", parsed.SenderName);
            Check.Equal("+5541998765432", parsed.SenderAddress);
            Check.Equal("Já saí. Te ligo.", parsed.Text);
            Check.Equal("UNREAD", parsed.Status);
            Check.Equal("TELECOM/MSG/INBOX", parsed.Folder);
        });

        yield return new TestCase("MAP: sessão completa contra um iPhone simulado", async () =>
        {
            var (a, b) = PipeEnd.CreatePair(fragment: 11);
            var phone = new FakePhone();
            var serverTask = new ObexServerSession(b, MapConstants.MasTarget, phone.HandleAsync, maxPacket: 512).RunAsync(CancellationToken.None);

            using var obex = new ObexClient(a, TimeSpan.FromSeconds(5));
            var map = new MapClient(obex);
            await map.ConnectAsync();
            Check.Equal("/telecom/msg", phone.Path, "navegou até a pasta de mensagens");

            await map.SetNotificationRegistrationAsync(true);
            Check.True(phone.NotificationsOn, "avisos ligados");

            var list = await map.GetMessagesListingAsync("inbox", 40);
            Check.Equal(3, list.Count);
            Check.Equal(40, phone.LastMaxListCount, "MaxListCount");

            var message = await map.GetMessageAsync("0A01");
            Check.Equal("Amanda", message.SenderName);
            Check.Equal(FakePhone.LongText, message.Text, "texto longo, vários pacotes");

            var handle = await map.PushMessageAsync("Amanda", "+5541998765432", "Tô chegando 🚗");
            Check.Equal("0B77", handle);
            Check.Equal("outbox", phone.PushedFolder);
            Check.Equal("Tô chegando 🚗", BMessageCodec.Parse(phone.PushedMessage!).Text);
            Check.Equal("+5541998765432", BMessageCodec.Parse(phone.PushedMessage!).RecipientAddress);

            await Check.ThrowsAsync<ObexException>(() => map.GetMessageAsync("FFFF"), "mensagem inexistente");
            await obex.DisconnectAsync();
            await serverTask;
        });

        yield return new TestCase("MAP: canal de avisos (MNS) recebe evento do iPhone", async () =>
        {
            var (phoneSide, pcSide) = PipeEnd.CreatePair(fragment: 6);
            var events = new List<MapEvent>();
            var server = new ObexServerSession(pcSide, MapConstants.MnsTarget, request =>
            {
                if (request.Opcode == ObexOp.Put && request.Type == MapConstants.TypeEventReport)
                {
                    events.AddRange(MapXml.ParseEventReport(Encoding.UTF8.GetString(request.Body)));
                    return Task.FromResult(ObexReply.Ok());
                }
                return Task.FromResult(ObexReply.Fail(ObexStatus.BadRequest));
            });
            var serverTask = server.RunAsync(CancellationToken.None);

            using var phone = new ObexClient(phoneSide, TimeSpan.FromSeconds(5));
            await phone.ConnectAsync(MapConstants.MnsTarget);
            await phone.PutAsync(new[]
            {
                ObexHeader.Ascii(ObexHeader.Type, MapConstants.TypeEventReport),
                new ObexHeader(ObexHeader.AppParameters, ObexAppParams.Build((0x0F, ObexAppParams.U8(0)))),
            }, Encoding.UTF8.GetBytes("<MAP-event-report version=\"1.0\"><event type=\"NewMessage\" handle=\"77\" folder=\"TELECOM/MSG/INBOX\" msg_type=\"SMS_GSM\"/></MAP-event-report>"));

            Check.Equal(1, events.Count);
            Check.Equal("77", events[0].Handle);
            await phone.DisconnectAsync();
            await serverTask;
        });
    }

    /// <summary>Um iPhone de mentira: responde ao MAP como o de verdade deveria.</summary>
    private sealed class FakePhone
    {
        public static readonly string LongText = string.Join(" ", Enumerable.Range(1, 400).Select(i => $"linha {i} com acentuação"));

        public string Path { get; private set; } = "";
        public bool NotificationsOn { get; private set; }
        public int LastMaxListCount { get; private set; }
        public string? PushedFolder { get; private set; }
        public string? PushedMessage { get; private set; }

        public Task<ObexReply> HandleAsync(ObexRequest request)
        {
            switch (request.Opcode)
            {
                case ObexOp.SetPath:
                    var name = request.Name ?? "";
                    if (name.Length == 0) { Path = ""; return Reply(ObexReply.Ok()); }
                    if ((Path == "" && name == "telecom") || (Path == "/telecom" && name == "msg"))
                    {
                        Path += "/" + name;
                        return Reply(ObexReply.Ok());
                    }
                    return Reply(ObexReply.Fail(ObexStatus.NotFound));

                case ObexOp.Get when request.Type == MapConstants.TypeListing:
                    LastMaxListCount = ObexAppParams.ReadU16(ObexAppParams.Parse(request.AppParameters)[MapConstants.TagMaxListCount]);
                    return Reply(new ObexReply { Body = Encoding.UTF8.GetBytes(Listing) });

                case ObexOp.Get when request.Type == MapConstants.TypeMessage:
                    if (request.Name != "0A01") return Reply(ObexReply.Fail(ObexStatus.NotFound));
                    var raw = "BEGIN:BMSG\r\nVERSION:1.0\r\nSTATUS:UNREAD\r\nTYPE:SMS_GSM\r\nFOLDER:TELECOM/MSG/INBOX\r\n" +
                              "BEGIN:VCARD\r\nVERSION:2.1\r\nFN:Amanda\r\nN:;Amanda\r\nTEL:+5541998765432\r\nEND:VCARD\r\n" +
                              "BEGIN:BENV\r\nBEGIN:BBODY\r\nCHARSET:UTF-8\r\nBEGIN:MSG\r\n" + LongText + "\r\nEND:MSG\r\nEND:BBODY\r\nEND:BENV\r\nEND:BMSG\r\n";
                    return Reply(new ObexReply { Body = Encoding.UTF8.GetBytes(raw) });

                case ObexOp.Put when request.Type == MapConstants.TypeNotificationRegistration:
                    NotificationsOn = ObexAppParams.Parse(request.AppParameters)[MapConstants.TagNotificationStatus][0] == 1;
                    return Reply(ObexReply.Ok());

                case ObexOp.Put when request.Type == MapConstants.TypeMessage:
                    PushedFolder = request.Name;
                    PushedMessage = Encoding.UTF8.GetString(request.Body);
                    var ok = ObexReply.Ok();
                    ok.Headers.Add(ObexHeader.Text(ObexHeader.Name, "0B77"));
                    return Reply(ok);

                default:
                    return Reply(ObexReply.Fail(ObexStatus.BadRequest));
            }
        }

        private static Task<ObexReply> Reply(ObexReply reply) => Task.FromResult(reply);
    }
}

public static class ContactTests
{
    private const string Cards =
        "BEGIN:VCARD\r\nVERSION:3.0\r\nN:Jussen;Lucas;;;\r\nFN:Lucas Jussen\r\nTEL;TYPE=CELL:+55 41 90000-0000\r\nEND:VCARD\r\n" +
        "BEGIN:VCARD\r\nVERSION:3.0\r\nN:Souza\\, Filho;José;;;\r\nFN:José Souza\\, Filho\r\nTEL;TYPE=HOME,VOICE:(41) 3333-4444\r\n" +
        "item1.TEL;type=CELL;type=pref:+5541988887777\r\nEMAIL;TYPE=INTERNET:jose@exemplo.com\r\n" +
        "PHOTO;ENCODING=b;TYPE=JPEG:" + "/9j/4AAQSkZJRgABAQAAAQABAAD/2wBDAAgGBgcGBQgHBwcJCQgKDBQNDAsLDBkSEw8UHRof\r\n Hh0aHBwgJC4nICIsIxwcKDcpLDAxNDQ0Hyc5PTgyPC4zNDL/wAALCAABAAEBAREA/8QAFAAB\r\n AAAAAAAAAAAAAAAAAAAAAP/EABQQAQAAAAAAAAAAAAAAAAAAAAD/2gAIAQEAAD8AVN//2Q==\r\n" +
        "END:VCARD\r\n";

    public static IEnumerable<TestCase> All()
    {
        yield return new TestCase("vCard 3.0: nome, telefones, e-mail e foto dobrada", () =>
        {
            var list = VCardParser.Parse(Cards);
            Check.Equal(2, list.Count);
            Check.Equal("Lucas Jussen", list[0].DisplayName);
            Check.Equal("celular", list[0].Phones[0].Kind);

            var jose = list[1];
            Check.Equal("José Souza, Filho", jose.DisplayName);
            Check.Equal("Souza, Filho", jose.LastName);
            Check.Equal(2, jose.Phones.Count);
            Check.Equal("casa", jose.Phones[0].Kind);
            Check.Equal("+5541988887777", jose.Phones[1].Number);
            Check.Equal("jose@exemplo.com", jose.Emails[0]);
            Check.True(jose.Photo is { Length: > 100 }, "foto decodificada");
            Check.Equal((byte)0xFF, jose.Photo![0]);
            Check.Equal((byte)0xD8, jose.Photo[1]);
        });

        yield return new TestCase("vCard 2.1: quoted-printable com quebra de linha", () =>
        {
            var text = "BEGIN:VCARD\r\nVERSION:2.1\r\nN;CHARSET=UTF-8;ENCODING=QUOTED-PRINTABLE:Concei=C3=A7=C3=A3o;Jo=\r\n=C3=A3o\r\nTEL;CELL:041999990000\r\nEND:VCARD\r\n";
            var entry = VCardParser.Parse(text).Single();
            Check.Equal("João Conceição", entry.DisplayName);
            Check.Equal("041999990000", entry.PrimaryAddress);
        });

        yield return new TestCase("vCard: histórico de ligações", () =>
        {
            var text = "BEGIN:VCARD\r\nVERSION:3.0\r\nFN:Amanda\r\nTEL:+5541998765432\r\nX-IRMC-CALL-DATETIME;TYPE=MISSED:20261116T183000\r\nEND:VCARD\r\n" +
                       "BEGIN:VCARD\r\nVERSION:2.1\r\nN:\r\nTEL:0800123456\r\nX-IRMC-CALL-DATETIME;DIALED:20261115T090000\r\nEND:VCARD\r\n";
            var list = VCardParser.Parse(text);
            Check.Equal("MISSED", list[0].CallKind);
            Check.Equal(new DateTime(2026, 11, 16, 18, 30, 0), list[0].CallTime!.Value);
            Check.Equal("DIALED", list[1].CallKind);
            Check.Equal("0800123456", list[1].DisplayName, "sem nome, mostra o número");
        });

        yield return new TestCase("PBAP: tamanho da agenda e download em fatias", async () =>
        {
            var (a, b) = PipeEnd.CreatePair(fragment: 13);
            var cards = Enumerable.Range(0, 60).Select(i => $"BEGIN:VCARD\r\nVERSION:3.0\r\nFN:Contato {i}\r\nTEL:+55419000{i:0000}\r\nEND:VCARD\r\n").ToArray();
            ulong lastFilter = 0;

            var server = new ObexServerSession(b, PbapConstants.Target, request =>
            {
                if (request.Opcode != ObexOp.Get || request.Type != PbapConstants.TypePhonebook || request.Name != PbapConstants.Contacts)
                    return Task.FromResult(ObexReply.Fail(ObexStatus.NotFound));

                var p = ObexAppParams.Parse(request.AppParameters);
                int max = ObexAppParams.ReadU16(p[PbapConstants.TagMaxListCount]);
                if (max == 0)
                {
                    var size = ObexReply.Ok();
                    size.Headers.Add(new ObexHeader(ObexHeader.AppParameters,
                        ObexAppParams.Build((PbapConstants.TagPhonebookSize, ObexAppParams.U16(cards.Length)))));
                    return Task.FromResult(size);
                }

                int offset = ObexAppParams.ReadU16(p[PbapConstants.TagListStartOffset]);
                lastFilter = p[PbapConstants.TagFilter].Aggregate(0UL, (acc, x) => (acc << 8) | x);
                return Task.FromResult(new ObexReply { Body = Encoding.UTF8.GetBytes(string.Concat(cards.Skip(offset).Take(max))) });
            }, maxPacket: 300);
            var serverTask = server.RunAsync(CancellationToken.None);

            using var obex = new ObexClient(a, TimeSpan.FromSeconds(5));
            var pbap = new PbapClient(obex);
            await pbap.ConnectAsync();
            Check.Equal(60, await pbap.GetSizeAsync(PbapConstants.Contacts));

            var page = VCardParser.Parse(await pbap.PullAsync(PbapConstants.Contacts, PbapConstants.FilterWithPhoto, 25, 50));
            Check.Equal(10, page.Count, "última fatia");
            Check.Equal("Contato 50", page[0].DisplayName);
            Check.Equal(PbapConstants.FilterWithPhoto, lastFilter, "filtro de campos");

            await obex.DisconnectAsync();
            await serverTask;
        });
    }
}
