using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Native.Core;
using Native.Protocol;

namespace Native.Bluetooth;

/// <summary>
/// Começa uma ligação no iPhone a partir do PC, pelo perfil Mãos-Livres (HFP).
///
/// O áudio de ligação no Windows é controlado pelo próprio sistema (não há API pública para
/// um programa receber esse áudio). Por isso o Native abre o canal de comandos, manda
/// discar e fecha logo em seguida: a ligação continua no iPhone (ou nos fones conectados a ele).
/// </summary>
public sealed class CallService
{
    private static readonly Guid HandsFreeAudioGateway = Guid.Parse("0000111F-0000-1000-8000-00805F9B34FB");

    private readonly Func<ulong?> _preferredAddress;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public CallService(Func<ulong?> preferredAddress)
    {
        _preferredAddress = preferredAddress;
    }

    public string NameFilter { get; set; } = "iPhone";

    /// <summary>Limpa o número para discagem: mantém dígitos, + inicial, * e #.</summary>
    public static string Sanitize(string number)
    {
        var sb = new StringBuilder();
        foreach (var c in number.Trim())
        {
            if (char.IsDigit(c) || c is '*' or '#') sb.Append(c);
            else if (c == '+' && sb.Length == 0) sb.Append(c);
        }
        return sb.ToString();
    }

    public async Task DialAsync(string number)
    {
        var digits = Sanitize(number);
        if (digits.Count(char.IsDigit) < 3) throw new ClassicException("Número inválido.");

        await _gate.WaitAsync();
        RfcommTransport? transport = null;
        try
        {
            Services.PhoneLinkGuard.Stop();
            transport = await RfcommTransport.OpenAsync(HandsFreeAudioGateway, "o canal de ligações", _preferredAddress(), NameFilter);
            var at = new AtChannel(transport);

            // Conexão mínima do Mãos-Livres: sem recursos extras, sem áudio.
            await at.CommandAsync("AT+BRSF=0");
            await at.CommandAsync("AT+CIND=?");
            await at.CommandAsync("AT+CIND?");
            await at.CommandAsync("AT+CMER=3,0,0,1");

            var reply = await at.CommandAsync($"ATD{digits};", TimeSpan.FromSeconds(10));
            if (!reply.Ok) throw new ClassicException("O iPhone não aceitou discar esse número.");
            Log.Info("HFP", "Ligação iniciada no iPhone");

            // Dá um instante para o iPhone registrar a chamada antes de soltar o canal.
            await Task.Delay(800);
        }
        catch (ClassicException ex)
        {
            Log.Warn("HFP", ex.Message);
            throw;
        }
        catch (Exception ex)
        {
            Log.Error("HFP", "Falha ao discar", ex);
            throw new ClassicException(
                "Não consegui usar o canal de ligações. Se o Windows estiver usando o iPhone como dispositivo de áudio, ligue pelo iPhone desta vez.");
        }
        finally
        {
            transport?.Dispose();
            _gate.Release();
        }
    }

    private sealed record AtReply(bool Ok, List<string> Lines);

    /// <summary>Conversa de texto "AT" (comando → linhas → OK/ERROR).</summary>
    private sealed class AtChannel
    {
        private readonly RfcommTransport _transport;
        private readonly StringBuilder _buffer = new();

        public AtChannel(RfcommTransport transport)
        {
            _transport = transport;
        }

        public async Task<AtReply> CommandAsync(string command, TimeSpan? timeout = null)
        {
            using var cts = new CancellationTokenSource(timeout ?? TimeSpan.FromSeconds(5));
            await _transport.WriteAsync(Encoding.ASCII.GetBytes(command + "\r"), cts.Token);

            var lines = new List<string>();
            while (true)
            {
                var line = await ReadLineAsync(cts.Token);
                if (line.Length == 0) continue;
                if (line == "OK") return new AtReply(true, lines);
                if (line == "ERROR" || line.StartsWith("+CME ERROR", StringComparison.Ordinal)) return new AtReply(false, lines);
                lines.Add(line);
            }
        }

        private async Task<string> ReadLineAsync(CancellationToken ct)
        {
            while (true)
            {
                var text = _buffer.ToString();
                int end = text.IndexOfAny(new[] { '\r', '\n' });
                if (end >= 0)
                {
                    _buffer.Remove(0, end + 1);
                    return text[..end].Trim();
                }
                var chunk = await _transport.ReadSomeAsync(256, ct);
                _buffer.Append(Encoding.ASCII.GetString(chunk));
            }
        }
    }
}
