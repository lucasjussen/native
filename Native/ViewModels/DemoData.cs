using System;
using System.Linq;
using Native.Bluetooth;
using Native.Models;
using Native.Protocol;

namespace Native.ViewModels;

/// <summary>
/// Dados de exemplo para ver a interface sem iPhone por perto (Native.exe --demo).
/// Usado em capturas de tela e para testar o visual; não toca no Bluetooth.
/// </summary>
public static class DemoData
{
    public static void Apply(MainViewModel vm)
    {
        var now = DateTime.Now;
        vm.SimulateState(LinkState.Connected, "Conectado", "iPhone de Lucas", "iPhone16,1");
        vm.SimulateCharging(81, 82);
        vm.SimulateNowPlaying(new NowPlaying("Spotify", "she's all i wanna be", "Tate McRae", "i used to think i could fly", 192, 147, 1, true, now, 0.5625));
        vm.SimulateMediaCommands(MediaCommand.Play, MediaCommand.Pause, MediaCommand.TogglePlayPause, MediaCommand.NextTrack,
            MediaCommand.PreviousTrack, MediaCommand.VolumeUp, MediaCommand.VolumeDown);

        uint uid = 1;
        AncsNotification N(string app, AncsCategory cat, string title, string subtitle, string message, double minutesAgo) =>
            new(uid++, cat, false, false, true, false, true, app, title, subtitle, message, now.AddMinutes(-minutesAgo));

        vm.SimulateNotification(N("com.apple.mobilephone", AncsCategory.MissedCall, "Mãe", "", "Ligação perdida", 52));
        vm.SimulateNotification(N("com.burbn.instagram", AncsCategory.Social, "Instagram", "", "marina.alves curtiu sua foto.", 34));
        vm.SimulateNotification(N("net.whatsapp.WhatsApp", AncsCategory.Social, "Família", "Pai", "Domingo tem almoço aqui, avisa se vem 🍝", 21));
        vm.SimulateNotification(N("net.whatsapp.WhatsApp", AncsCategory.Social, "Rafael", "", "Viu o jogo ontem?", 12));
        vm.SimulateNotification(N("net.whatsapp.WhatsApp", AncsCategory.Social, "Amanda", "", "Chego em 10 min ❤️", 4));
        vm.SimulateNotification(N("com.nubank.nubank", AncsCategory.BusinessAndFinance, "Nubank", "", "Seu código de verificação é 482913. Não compartilhe com ninguém.", 2));
        vm.SimulateNotification(N("com.apple.MobileSMS", AncsCategory.Social, "Amanda", "", "Pode pegar o pão no caminho?", 1));

        vm.Messages.SimulateConversations(new[]
        {
            Conversation("Amanda", "+5541998765432", "Pode pegar o pão no caminho?", now.AddMinutes(-1), unread: true, outgoing: false),
            Conversation("Rafael", "+5541991112222", "Fechado, sábado às 15h então", now.AddHours(-3), unread: false, outgoing: true),
            Conversation("Mãe", "+5541993334444", "Me liga quando puder", now.AddDays(-1), unread: false, outgoing: false),
            Conversation("29000", "29000", "Seu código de acesso é 551200", now.AddDays(-2), unread: false, outgoing: false),
            Conversation("João Pedro", "jp@exemplo.com", "Te mandei o arquivo por e-mail", now.AddDays(-5), unread: false, outgoing: false),
        });

        vm.Phone.SimulateRecents(new[]
        {
            new CallRecord("Mãe", "+5541993334444", "MISSED", now.AddMinutes(-52)),
            new CallRecord("Amanda", "+5541998765432", "DIALED", now.AddHours(-2)),
            new CallRecord("Rafael", "+5541991112222", "RECEIVED", now.AddDays(-1)),
            new CallRecord("0800 777 0000", "08007770000", "RECEIVED", now.AddDays(-3)),
        });
    }

    /// <summary>Uma notificação "chegando agora", para ver o banner.</summary>
    public static void Incoming(MainViewModel vm, string kind)
    {
        var n = kind == "codigo"
            ? new AncsNotification(900, AncsCategory.Social, false, false, false, false, true, "com.apple.MobileSMS",
                "29000", "", "Seu código de acesso é 551200. Válido por 5 minutos.", DateTime.Now)
            : new AncsNotification(901, AncsCategory.Social, false, false, false, false, true, "com.apple.MobileSMS",
                "Amanda", "", "Já saí daqui, chego em 15 min. Precisa de alguma coisa do mercado?", DateTime.Now);
        vm.SimulateNotification(n);
    }

    private static ConversationSummary Conversation(string name, string address, string preview, DateTime when, bool unread, bool outgoing) =>
        new(TextMatch.AddressKey(address), name, address, preview, when, unread, outgoing,
            new[] { new MapListedMessage("", preview, when, name, address, "", "SMS_GSM", !unread, outgoing) });
}
