using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Native.Protocol.Share;

namespace Native.Tests;

/// <summary>Compartilhamento de arquivos: QR code, mDNS e o servidor HTTP de verdade (em localhost).</summary>
public static class ShareTests
{
    public static IEnumerable<TestCase> All()
    {
        yield return new TestCase("QR: escolhe a versão certa e desenha os localizadores", () =>
        {
            var small = QrCode.Encode("hi");
            Check.Equal(1, small.Version, "versão de 'hi'");
            Check.Equal(21, small.Size, "tamanho v1");
            var url = QrCode.Encode("http://192.168.100.123:47811/?k=AbCdEf0123456789xyzQW1");
            Check.Equal(4, url.Version, "versão da URL do pareamento");
            foreach (var (r, c) in new[] { (0, 0), (0, url.Size - 1), (url.Size - 1, 0) })
                Check.True(url[r, c], $"canto do localizador em {r},{c}");
            Check.True(!url[1, 1] && url[2, 2], "anel claro e miolo escuro do localizador");
            Check.Equal(url.ToAscii(), QrCode.Encode("http://192.168.100.123:47811/?k=AbCdEf0123456789xyzQW1").ToAscii(), "determinístico");
        });

        yield return new TestCase("mDNS: entende a pergunta e responde com o IP", () =>
        {
            var name = MdnsPacket.HostNameFor("LUCAS-PC");
            Check.Equal("native-lucas-pc.local", name, "nome .local");
            Check.Equal("native-pc-do-jo-o.local", MdnsPacket.HostNameFor("PC do João"), "nome com espaço e acento");

            var query = new List<byte> { 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0 };
            foreach (var label in name.Split('.')) { query.Add((byte)label.Length); query.AddRange(Encoding.ASCII.GetBytes(label)); }
            query.AddRange(new byte[] { 0, 0, 1, 0x80, 1 });   // A, IN, resposta unicast
            var (_, qs) = MdnsPacket.ParseQuery(query.ToArray());
            Check.Equal(1, qs.Count, "perguntas");
            Check.Equal(name, qs[0].Name, "nome perguntado");
            Check.True(qs[0].UnicastResponse, "bit de resposta unicast");

            var answer = MdnsPacket.BuildAnswer(0, name, IPAddress.Parse("192.168.0.42"));
            Check.Bytes(new byte[] { 192, 168, 0, 42 }, answer[^4..], "IP no fim da resposta");
            Check.Equal(0x84, (int)answer[2], "flags de resposta autoritativa");
            Check.Equal(0, MdnsPacket.ParseQuery(answer).Questions.Count, "resposta não é tratada como pergunta");
        });

        yield return new TestCase("Nomes de arquivo seguros para o Windows", () =>
        {
            var now = new DateTime(2026, 11, 17, 11, 40, 5);
            Check.Equal("passwd", FileNames.Clean("../../etc/passwd", null, now), "sem caminho");
            Check.Equal("a_b_.txt", FileNames.Clean("a:b?.txt", null, now), "caracteres proibidos");
            Check.Equal("Foto do iPhone 2026-11-17 11.40.05.jpg", FileNames.Clean(null, "image/jpeg", now), "sem nome, foto");
            Check.Equal("IMG_0001.heic", FileNames.Clean("IMG_0001", "image/heic", now), "extensão pelo tipo");
            Check.Equal("Relatório final.pdf", FileNames.Clean("Relat%C3%B3rio%20final.pdf", null, now), "nome codificado");
            Check.Equal("_CON.txt", FileNames.Clean("CON.txt", null, now), "nome reservado");
        });

        yield return new TestCase("Bateria: percebe quando o iPhone está carregando", () =>
        {
            var t = new Native.Protocol.ChargeTracker();
            var t0 = new DateTime(2026, 11, 17, 19, 0, 0);
            Check.Equal(Native.Protocol.ChargeState.Unknown, t.Update(40, t0), "primeira leitura não diz nada");
            Check.Equal(Native.Protocol.ChargeState.Charging, t.Update(41, t0.AddMinutes(2)), "subiu: carregando");
            Check.Equal(Native.Protocol.ChargeState.Charging, t.Update(41, t0.AddMinutes(6)), "parado um pouco: continua");
            Check.Equal(Native.Protocol.ChargeState.Unknown, t.Tick(t0.AddMinutes(40)), "parado muito tempo: deixa de afirmar");
            Check.Equal(Native.Protocol.ChargeState.Discharging, t.Update(40, t0.AddMinutes(41)), "caiu: fora da tomada");
            t.Update(99, t0.AddMinutes(90));
            Check.Equal(Native.Protocol.ChargeState.Full, t.Update(100, t0.AddMinutes(95)), "chegou a 100%");
        });

        yield return new TestCase("Área de transferência: texto, RTF e imagem vindos do iPhone", () =>
        {
            var t = ClipData.FromBody(Encoding.UTF8.GetBytes("  olá, mundo \n"), "text/plain; charset=utf-8");
            Check.Equal("olá, mundo", t.Text, "texto aparado");
            Check.True(t.Image is null && !t.IsRtf, "é texto comum");
            var r = ClipData.FromBody(Encoding.ASCII.GetBytes("{\\rtf1\\ansi Oi}"), "application/octet-stream");
            Check.True(r.IsRtf, "reconhece RTF pelo conteúdo");
            var png = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0 };
            var i = ClipData.FromBody(png, "application/octet-stream");
            Check.Equal("image/png", i.ImageType, "reconhece PNG sem o tipo certo");
            Check.True(ClipData.FromBody(Array.Empty<byte>(), null).IsEmpty, "vazio é vazio");
        });

        yield return new TestCase("Servidor: área de transferência nos dois sentidos", ClipRoundTrip);

        yield return new TestCase("Servidor: chave, envio do iPhone, download e pulo de vídeo (Range)", ServerRoundTrip);
    }

    private static async Task ServerRoundTrip()
    {
        var folder = Path.Combine(Path.GetTempPath(), "native-teste-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(folder);
        var token = ShareServer.NewToken();
        using var server = new ShareServer(token, () => folder) { DisplayName = "PC de teste" };
        server.Start(47900 + Random.Shared.Next(0, 500), IPAddress.Loopback);
        var baseUrl = $"http://127.0.0.1:{server.Port}";
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };

        try
        {
            // sem chave
            Check.Equal(HttpStatusCode.Unauthorized, (await http.GetAsync(baseUrl + "/api/state")).StatusCode, "sem chave");
            Check.Equal(HttpStatusCode.Unauthorized, (await http.GetAsync(baseUrl + "/api/state?k=errada")).StatusCode, "chave errada");
            var page = await http.GetStringAsync(baseUrl + "/");
            Check.True(page.Contains("Native"), "página do iPhone");

            // iPhone → PC (Content-Length)
            var received = new List<ReceivedFile>();
            server.FileReceived += f => { lock (received) received.Add(f); };
            var photo = Enumerable.Range(0, 300_000).Select(i => (byte)(i * 7)).ToArray();
            var content = new ByteArrayContent(photo);
            content.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
            var r = await http.PostAsync($"{baseUrl}/api/upload?k={token}&name={Uri.EscapeDataString("Praia ☀️.jpg")}", content);
            Check.Equal(HttpStatusCode.OK, r.StatusCode, "upload");
            Check.Bytes(photo, await File.ReadAllBytesAsync(Path.Combine(folder, "Praia ☀️.jpg")), "conteúdo recebido");

            // mesmo nome de novo, agora com corpo "chunked" (tamanho desconhecido), como alguns apps mandam
            var chunked = new StreamContent(new UnknownLengthStream(photo));
            var req = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/api/upload?k={token}") { Content = chunked };
            req.Headers.Add("X-Filename", Uri.EscapeDataString("Praia ☀️.jpg"));
            req.Headers.TransferEncodingChunked = true;
            Check.Equal(HttpStatusCode.OK, (await http.SendAsync(req)).StatusCode, "upload chunked");
            Check.True(File.Exists(Path.Combine(folder, "Praia ☀️ (2).jpg")), "não sobrescreve: cria (2)");
            Check.Equal(2, received.Count, "eventos de recebido");
            Check.True(!Directory.GetFiles(folder, "*.part").Any(), "sem arquivos temporários sobrando");

            // texto
            string? text = null;
            server.TextReceived += t => text = t;
            await http.PostAsync($"{baseUrl}/api/text?k={token}", new StringContent("https://apple.com", Encoding.UTF8));
            Check.Equal("https://apple.com", text, "texto recebido");

            // PC → iPhone
            var doc = Path.Combine(folder, "contrato.pdf");
            await File.WriteAllBytesAsync(doc, photo);
            var item = server.OfferFile(doc);
            ShareItem? delivered = null;
            server.ItemDelivered += i => delivered = i;

            var state = JsonDocument.Parse(await http.GetStringAsync($"{baseUrl}/api/state?k={token}")).RootElement;
            Check.Equal("PC de teste", state.GetProperty("name").GetString(), "nome do PC");
            Check.Equal(item.Id, state.GetProperty("items")[0].GetProperty("id").GetString(), "item na lista");
            Check.True(server.LastPhoneSeen > DateTime.Now.AddSeconds(-5), "registra que o iPhone apareceu");

            var partial = new HttpRequestMessage(HttpMethod.Get, $"{baseUrl}/api/file/{item.Id}?k={token}");
            partial.Headers.Range = new RangeHeaderValue(1000, 1999);
            var pr = await http.SendAsync(partial);
            Check.Equal(HttpStatusCode.PartialContent, pr.StatusCode, "Range");
            Check.Bytes(photo[1000..2000], await pr.Content.ReadAsByteArrayAsync(), "pedaço do arquivo");
            Check.Null(delivered, "pedaço não conta como entregue");

            var full = await http.GetAsync($"{baseUrl}/api/file/{item.Id}?k={token}&dl=1");
            Check.Bytes(photo, await full.Content.ReadAsByteArrayAsync(), "download completo");
            Check.True(full.Content.Headers.ContentDisposition?.DispositionType == "attachment", "baixa como anexo");
            Check.Equal(item.Id, delivered?.Id, "marcado como entregue");

            await http.PostAsync($"{baseUrl}/api/remove/{item.Id}?k={token}", null);
            Check.Equal(0, server.Outbox.Count, "removido pelo iPhone");
            Check.Equal(HttpStatusCode.NotFound, (await http.GetAsync($"{baseUrl}/api/file/{item.Id}?k={token}")).StatusCode, "não existe mais");
        }
        finally
        {
            server.Stop();
            try { Directory.Delete(folder, true); } catch { }
        }
    }

    private static async Task ClipRoundTrip()
    {
        var token = ShareServer.NewToken();
        using var server = new ShareServer(token, () => Path.GetTempPath());
        server.Start(48500 + Random.Shared.Next(0, 400), IPAddress.Loopback);
        var url = $"http://127.0.0.1:{server.Port}/api/clip?k={token}";
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        try
        {
            Check.Equal(HttpStatusCode.Forbidden, (await http.GetAsync(url)).StatusCode, "desligado no PC");

            ClipData? pc = null;
            server.ClipboardProvider = () => Task.FromResult(pc);
            Check.Equal(HttpStatusCode.NoContent, (await http.GetAsync(url)).StatusCode, "nada copiado");
            pc = new ClipData("senha do wi-fi: café123", null);
            Check.Equal("senha do wi-fi: café123", await http.GetStringAsync(url), "PC → iPhone");

            ClipData? received = null;
            server.ClipboardReceived += c => received = c;
            await http.PostAsync(url, new StringContent("link copiado no iPhone", Encoding.UTF8, "text/plain"));
            Check.Equal("link copiado no iPhone", received?.Text, "iPhone → PC");
            Check.Equal(HttpStatusCode.Unauthorized, (await http.GetAsync(url.Replace(token, "errada"))).StatusCode, "sem a chave não lê");
        }
        finally
        {
            server.Stop();
        }
    }

    /// <summary>Stream que não informa o tamanho, para forçar o envio "chunked".</summary>
    private sealed class UnknownLengthStream : Stream
    {
        private readonly MemoryStream _inner;
        public UnknownLengthStream(byte[] data) => _inner = new MemoryStream(data);
        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, Math.Min(count, 7000));
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
