using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Native.Core;
using Native.Protocol;

namespace Native.Bluetooth;

public sealed record ThreadMessage(string Handle, string Text, DateTime When, bool Outgoing);

public sealed record ConversationSummary(
    string Key, string Name, string Address, string Preview, DateTime When, bool Unread, bool LastIsOutgoing,
    IReadOnlyList<MapListedMessage> Messages);

/// <summary>
/// Mensagens do iPhone (SMS e iMessage) pelo Bluetooth clássico (MAP).
/// Abre a sessão sob demanda, reaproveita enquanto estiver em uso e fecha depois de um tempo parado.
/// </summary>
public sealed class MessageService : IDisposable
{
    private static readonly TimeSpan IdleClose = TimeSpan.FromSeconds(90);

    private readonly Func<ulong?> _preferredAddress;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private ObexClient? _obex;
    private MapClient? _map;
    private Timer? _idle;

    public MessageService(Func<ulong?> preferredAddress)
    {
        _preferredAddress = preferredAddress;
    }

    public string NameFilter { get; set; } = "iPhone";

    /// <summary>Conversas recentes, montadas a partir da caixa de entrada e dos enviados.</summary>
    public Task<List<ConversationSummary>> LoadConversationsAsync(int perFolder = 80) => RunAsync(async map =>
    {
        var inbox = await map.GetMessagesListingAsync("inbox", perFolder);
        List<MapListedMessage> sent;
        try { sent = await map.GetMessagesListingAsync("sent", perFolder); }
        catch (ObexException) { sent = new List<MapListedMessage>(); }   // alguns iPhones não expõem "enviados"

        var all = inbox.Select(m => (Message: m, Address: m.SenderAddress, Outgoing: false))
            .Concat(sent.Select(m => (Message: m, Address: m.RecipientAddress, Outgoing: true)))
            .Where(x => x.Address.Length > 0);

        return all
            .GroupBy(x => TextMatch.AddressKey(x.Address))
            .Select(g =>
            {
                var ordered = g.OrderByDescending(x => x.Message.When).ToList();
                var latest = ordered[0];
                var name = ordered.Select(x => x.Message.SenderName).FirstOrDefault(n => n.Length > 0 && !TextMatch.LooksLikePhone(n)) ?? "";
                return new ConversationSummary(
                    g.Key, name, latest.Address, latest.Message.Subject, latest.Message.When,
                    ordered.Any(x => !x.Outgoing && !x.Message.Read), latest.Outgoing,
                    ordered.Select(x => x.Message with { Sent = x.Outgoing }).ToList());
            })
            .OrderByDescending(c => c.When)
            .ToList();
    });

    /// <summary>Texto completo das últimas mensagens de uma conversa (a lista só traz o começo).</summary>
    public Task<List<ThreadMessage>> LoadThreadAsync(ConversationSummary conversation, int max = 25) => RunAsync(async map =>
    {
        var result = new List<ThreadMessage>();
        foreach (var listed in conversation.Messages.Take(max))
        {
            string text = listed.Subject;
            try
            {
                var full = await map.GetMessageAsync(listed.Handle);
                if (full.Text.Length > 0) text = full.Text;
            }
            catch (ObexException)
            {
                // Fica com o resumo da lista.
            }
            result.Add(new ThreadMessage(listed.Handle, text, listed.When, listed.Sent));
        }
        result.Reverse();
        return result;
    });

    /// <summary>Responde a quem mandou a notificação (o título é o nome, número ou e-mail).</summary>
    public Task ReplyToAsync(string notificationTitle, string text) => RunAsync(async map =>
    {
        var recent = await map.GetMessagesListingAsync("inbox", 50);
        var sender = MapMatch.FindSender(recent, notificationTitle)
            ?? throw new ClassicException($"Não achei \"{notificationTitle}\" nas mensagens recentes do iPhone.");
        await map.PushMessageAsync(sender.SenderName, sender.SenderAddress, text);
        Log.Info("MAP", "Resposta enviada");
        return true;
    });

    public Task SendAsync(string name, string address, string text) => RunAsync(async map =>
    {
        await map.PushMessageAsync(name, address, text);
        Log.Info("MAP", "Mensagem enviada");
        return true;
    });

    // ---------------------------------------------------------------- sessão

    private async Task<T> RunAsync<T>(Func<MapClient, Task<T>> work)
    {
        await _gate.WaitAsync();
        try
        {
            _idle?.Change(Timeout.Infinite, Timeout.Infinite);
            for (int attempt = 1; ; attempt++)
            {
                try
                {
                    var map = await EnsureSessionAsync();
                    return await work(map);
                }
                catch (Exception ex) when (attempt == 1 && ex is TimeoutException or ClassicException or System.IO.IOException or System.Runtime.InteropServices.COMException)
                {
                    // Sessão velha (o iPhone dormiu, saiu de alcance…): abre outra e tenta uma vez mais.
                    Log.Warn("MAP", $"Refazendo a sessão: {Log.FirstLine(ex.Message)}");
                    Close();
                }
            }
        }
        catch (ObexException ex)
        {
            Log.Warn("MAP", ex.Message);
            Close();
            throw new ClassicException(Friendly(ex));
        }
        catch (ClassicException)
        {
            Close();
            throw;
        }
        catch (Exception ex)
        {
            Log.Error("MAP", "Falha nas mensagens", ex);
            Close();
            throw new ClassicException("Não foi possível falar com o iPhone agora. Tente de novo.");
        }
        finally
        {
            _idle ??= new Timer(_ => IdleTimeout());
            _idle.Change(IdleClose, Timeout.InfiniteTimeSpan);
            _gate.Release();
        }
    }

    private async Task<MapClient> EnsureSessionAsync()
    {
        if (_map is not null) return _map;

        Services.PhoneLinkGuard.Stop();
        var transport = await RfcommTransport.OpenAsync(MapConstants.MasService, "o acesso às mensagens",
            _preferredAddress(), NameFilter);
        var obex = new ObexClient(transport, TimeSpan.FromSeconds(20));
        var map = new MapClient(obex);
        try
        {
            await map.ConnectAsync();
        }
        catch
        {
            obex.Dispose();
            throw;
        }

        _obex = obex;
        _map = map;
        Log.Info("MAP", "Sessão de mensagens aberta");
        return map;
    }

    private static string Friendly(ObexException ex) => ex.Code switch
    {
        ObexStatus.Forbidden or ObexStatus.Unauthorized =>
            "O iPhone não liberou as mensagens. No iPhone: Ajustes > Bluetooth > (i) ao lado do PC > ative \"Mostrar Notificações\".",
        ObexStatus.ServiceUnavailable or ObexStatus.NotImplemented =>
            "O iPhone recusou enviar por este PC. Responda pelo iPhone desta vez.",
        _ => $"O iPhone recusou o pedido ({ex.Message}).",
    };

    private void IdleTimeout()
    {
        if (!_gate.Wait(0)) return;
        try
        {
            if (_obex is not null) Log.Info("MAP", "Sessão de mensagens fechada por inatividade");
            Close(graceful: true);
        }
        finally
        {
            _gate.Release();
        }
    }

    private void Close(bool graceful = false)
    {
        var obex = _obex;
        _obex = null;
        _map = null;
        if (obex is null) return;
        if (graceful)
        {
            try { obex.DisconnectAsync().Wait(TimeSpan.FromSeconds(3)); } catch { }
        }
        obex.Dispose();
    }

    public void Dispose()
    {
        _idle?.Dispose();
        Close(graceful: true);
    }
}
