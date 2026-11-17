using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows.Media;
using Native.Core;
using Native.Protocol;

namespace Native.Models;

/// <summary>Um app do iPhone que já mandou notificação (nome, ícone e permissão).</summary>
public sealed class AppInfo : ObservableObject
{
    private static readonly string[] Palette =
        { "#0A84FF", "#30D158", "#5E5CE6", "#FF9F0A", "#FF375F", "#BF5AF2", "#FF453A", "#40C8E0", "#64D2FF", "#FFD60A" };

    public AppInfo(string appId, string displayName)
    {
        AppId = appId;
        _displayName = displayName;
        _fallbackBrush = BrushFor(appId, null);
    }

    public string AppId { get; }

    private string _displayName;
    public string DisplayName
    {
        get => _displayName;
        set { if (Set(ref _displayName, value)) Raise(nameof(Initial)); }
    }

    public string Initial => DisplayName.Length > 0 ? char.ToUpperInvariant(DisplayName[0]).ToString() : "?";

    private ImageSource? _icon;
    public ImageSource? Icon { get => _icon; set => Set(ref _icon, value); }

    private Brush _fallbackBrush;
    public Brush FallbackBrush { get => _fallbackBrush; set => Set(ref _fallbackBrush, value); }

    private bool _isAllowed = true;
    public bool IsAllowed { get => _isAllowed; set => Set(ref _isAllowed, value); }

    public static Brush BrushFor(string key, string? hex)
    {
        if (hex is null)
        {
            int h = 17;
            foreach (var c in key) h = unchecked(h * 31 + c);
            hex = Palette[(h & 0x7FFFFFFF) % Palette.Length];
        }
        var top = (Color)ColorConverter.ConvertFromString(hex);
        var light = Color.FromRgb(
            (byte)Math.Min(255, top.R + 40), (byte)Math.Min(255, top.G + 40), (byte)Math.Min(255, top.B + 40));
        var brush = new LinearGradientBrush(light, top, 90);
        brush.Freeze();
        return brush;
    }
}

/// <summary>Uma notificação vinda do iPhone, com o estado da resposta rápida.</summary>
public sealed class PhoneNotification : ObservableObject
{
    public PhoneNotification(AncsNotification n, AppInfo app)
    {
        Uid = n.Uid;
        App = app;
        Apply(n);
    }

    public uint Uid { get; }
    public AppInfo App { get; }

    public AncsCategory Category { get; private set; }
    public string Title { get; private set; } = "";
    public string Subtitle { get; private set; } = "";
    public string Message { get; private set; } = "";
    public DateTime ReceivedAt { get; private set; }
    public bool HasPositiveAction { get; private set; }
    public bool HasNegativeAction { get; private set; }

    /// <summary>Foto do contato (quando a agenda do iPhone está sincronizada).</summary>
    private ImageSource? _contactPhoto;
    public ImageSource? ContactPhoto
    {
        get => _contactPhoto;
        set { if (Set(ref _contactPhoto, value)) Raise(nameof(HasContactPhoto)); }
    }
    public bool HasContactPhoto => ContactPhoto is not null;

    public string DisplayTitle => string.IsNullOrWhiteSpace(Title) ? App.DisplayName : Title;
    public bool HasSubtitle => !string.IsNullOrWhiteSpace(Subtitle);
    public bool HasMessage => !string.IsNullOrWhiteSpace(Message);
    public bool IsCall => Category == AncsCategory.IncomingCall;
    public bool IsMissedCall => Category == AncsCategory.MissedCall;
    public string TimeLabel => TimeFormat.Relative(ReceivedAt);

    /// <summary>Código de verificação (2FA) encontrado no texto, como o macOS faz no Preenchimento Automático.</summary>
    public string? Code { get; private set; }
    public bool HasCode => Code is not null;
    public string CopyCodeLabel => Code is null ? "" : $"Copiar código {Code}";

    private bool _codeCopied;
    public bool CodeCopied
    {
        get => _codeCopied;
        set { if (Set(ref _codeCopied, value)) Raise(nameof(ShowCopyCodeButton)); }
    }
    public bool ShowCopyCodeButton => HasCode && !CodeCopied;

    public string FullText => string.Join("\n", new[] { DisplayTitle, Subtitle, Message }.Where(s => !string.IsNullOrWhiteSpace(s)));

    public void Apply(AncsNotification n)
    {
        Category = n.Category;
        Title = n.Title;
        Subtitle = n.Subtitle;
        Message = n.Message;
        ReceivedAt = n.ReceivedAt;
        HasPositiveAction = n.HasPositiveAction;
        HasNegativeAction = n.HasNegativeAction;
        Code = VerificationCode.Find(Title, Subtitle, Message);
        Raise(string.Empty);
    }

    public void RefreshTime() => Raise(nameof(TimeLabel));

    // ------------------------------------------------------------ responder

    /// <summary>Envia a resposta (definido pelo app). Devolve null se deu certo, ou a mensagem de erro.</summary>
    public static Func<PhoneNotification, string, Task<string?>>? ReplySender { get; set; }

    /// <summary>Abre o app de desktop/web correspondente (WhatsApp, Instagram…).</summary>
    public static Action<PhoneNotification>? ExternalOpener { get; set; }

    /// <summary>Liga de volta para quem ligou (definido pelo app).</summary>
    public static Action<PhoneNotification>? CallBack { get; set; }

    private static readonly Dictionary<string, string> ExternalApps = new(StringComparer.OrdinalIgnoreCase)
    {
        ["net.whatsapp.WhatsApp"] = "WhatsApp",
        ["net.whatsapp.WhatsAppSMB"] = "WhatsApp",
        ["com.burbn.instagram"] = "Instagram",
        ["ph.telegra.Telegraph"] = "Telegram",
        ["com.facebook.Messenger"] = "Messenger",
    };

    public bool CanReplyInline => !IsCall && string.Equals(App.AppId, "com.apple.MobileSMS", StringComparison.OrdinalIgnoreCase);
    public string? ExternalAppName => ExternalApps.TryGetValue(App.AppId, out var name) ? name : null;
    public bool HasExternalReply => ExternalAppName is not null;
    public string ExternalReplyLabel => ExternalAppName is null ? "" : $"Responder no {ExternalAppName}";
    public bool CanCallBack => IsMissedCall && CallBack is not null;
    public string ReplyPlaceholder => $"Mensagem para {DisplayTitle}";

    private bool _isReplying;
    public bool IsReplying
    {
        get => _isReplying;
        set { if (Set(ref _isReplying, value)) Raise(nameof(ShowReplyButtons)); }
    }

    private string _replyText = "";
    public string ReplyText
    {
        get => _replyText;
        set { if (Set(ref _replyText, value)) { Raise(nameof(HasReplyText)); Raise(nameof(CanSend)); } }
    }
    public bool HasReplyText => ReplyText.Length > 0;

    private bool _isSending;
    public bool IsSending
    {
        get => _isSending;
        private set { if (Set(ref _isSending, value)) { Raise(nameof(IsNotSending)); Raise(nameof(CanSend)); } }
    }
    public bool IsNotSending => !IsSending;
    public bool CanSend => !IsSending && ReplyText.Trim().Length > 0;

    private bool _replySent;
    public bool ReplySent
    {
        get => _replySent;
        private set { if (Set(ref _replySent, value)) Raise(nameof(ShowReplyButtons)); }
    }

    private string _replyStatus = "";
    public string ReplyStatus
    {
        get => _replyStatus;
        private set { if (Set(ref _replyStatus, value)) Raise(nameof(HasReplyStatus)); }
    }
    public bool HasReplyStatus => ReplyStatus.Length > 0;

    private bool _replyFailed;
    public bool ReplyFailed { get => _replyFailed; private set => Set(ref _replyFailed, value); }

    public bool ShowReplyButtons => !IsReplying && !ReplySent && (CanReplyInline || HasExternalReply || CanCallBack);

    private RelayCommand? _startReply, _cancelReply, _sendReply, _openExternal, _callBack;
    public ICommand StartReplyCommand => _startReply ??= new RelayCommand(() =>
    {
        ReplyStatus = "";
        ReplyFailed = false;
        IsReplying = true;
    });
    public ICommand CancelReplyCommand => _cancelReply ??= new RelayCommand(() =>
    {
        if (IsSending) return;
        IsReplying = false;
        ReplyStatus = "";
    });
    public ICommand SendReplyCommand => _sendReply ??= new RelayCommand(async () => await SendReplyAsync());
    public ICommand OpenExternalCommand => _openExternal ??= new RelayCommand(() => ExternalOpener?.Invoke(this));
    public ICommand CallBackCommand => _callBack ??= new RelayCommand(() => CallBack?.Invoke(this));

    private async Task SendReplyAsync()
    {
        if (!CanSend || ReplySender is null) return;
        IsSending = true;
        ReplyFailed = false;
        ReplyStatus = "Enviando…";

        var error = await ReplySender(this, ReplyText.Trim());

        IsSending = false;
        if (error is null)
        {
            ReplySent = true;
            IsReplying = false;
            ReplyText = "";
            ReplyStatus = "Mensagem enviada";
        }
        else
        {
            ReplyFailed = true;
            ReplyStatus = error;
        }
    }

    /// <summary>Aviso curto embaixo da notificação (ex.: "nome copiado").</summary>
    public void ShowHint(string text, bool failed = false)
    {
        ReplyFailed = failed;
        ReplyStatus = text;
    }
}

/// <summary>Pilha de notificações de um mesmo app, como na Central de Notificações do macOS.</summary>
public sealed class NotificationGroup : ObservableObject
{
    public NotificationGroup(AppInfo app)
    {
        App = app;
        Items.CollectionChanged += (_, _) => Changed();
    }

    public AppInfo App { get; }
    public ObservableCollection<PhoneNotification> Items { get; } = new();

    private bool _isExpanded;
    public bool IsExpanded
    {
        get => _isExpanded;
        set { if (Set(ref _isExpanded, value)) Changed(); }
    }

    public PhoneNotification? Latest => Items.FirstOrDefault();
    public DateTime LatestTime => Latest?.ReceivedAt ?? DateTime.MinValue;

    public IReadOnlyList<PhoneNotification> VisibleItems =>
        IsExpanded ? Items.ToList() : Items.Take(1).ToList();

    public bool HasMore => !IsExpanded && Items.Count > 1;
    public bool ShowSecondLayer => !IsExpanded && Items.Count > 2;
    public bool CanCollapse => IsExpanded && Items.Count > 1;
    public string MoreLabel => Items.Count - 1 == 1 ? "Mais 1 notificação" : $"Mais {Items.Count - 1} notificações";

    public void Changed()
    {
        if (Items.Count <= 1 && _isExpanded) { _isExpanded = false; Raise(nameof(IsExpanded)); }
        Raise(nameof(Latest));
        Raise(nameof(VisibleItems));
        Raise(nameof(HasMore));
        Raise(nameof(ShowSecondLayer));
        Raise(nameof(CanCollapse));
        Raise(nameof(MoreLabel));
    }

    public void RefreshTimes()
    {
        foreach (var n in Items) n.RefreshTime();
    }
}

/// <summary>Uma pessoa (contato, conversa ou ligação), com foto ou iniciais.</summary>
public abstract class PersonItem : ObservableObject
{
    private string _name = "";
    public string Name
    {
        get => _name;
        set
        {
            if (!Set(ref _name, value)) return;
            Raise(nameof(Initials));
            Raise(nameof(AvatarBrush));
        }
    }

    private ImageSource? _photo;
    public ImageSource? Photo
    {
        get => _photo;
        set { if (Set(ref _photo, value)) Raise(nameof(HasPhoto)); }
    }

    public bool HasPhoto => Photo is not null;
    public string Initials => TextMatch.Initials(Name);
    public Brush AvatarBrush => AppInfo.BrushFor(Name, "#8E8E93");
}

public sealed class ConversationItem : PersonItem
{
    public string Key { get; init; } = "";
    public string Address { get; init; } = "";
    public string Preview { get; init; } = "";
    public DateTime When { get; init; }
    public bool Unread { get; init; }
    public bool LastIsOutgoing { get; init; }
    public object? Source { get; init; }

    public string PreviewLine => LastIsOutgoing ? $"Você: {Preview}" : Preview;
    public string TimeLabel => TimeFormat.Relative(When);
}

public sealed class MessageBubble : ObservableObject
{
    public string Text { get; init; } = "";
    public DateTime When { get; init; }
    public bool Outgoing { get; init; }

    private string _status = "";
    public string Status { get => _status; set { if (Set(ref _status, value)) Raise(nameof(HasStatus)); } }
    public bool HasStatus => Status.Length > 0;

    private bool _failed;
    public bool Failed { get => _failed; set => Set(ref _failed, value); }

    /// <summary>Mostra a data acima do balão quando muda o dia ou passa muito tempo.</summary>
    public bool ShowTimestamp { get; set; }
    public string TimestampLabel => When.Date == DateTime.Today
        ? $"Hoje {When:HH:mm}"
        : When.Date == DateTime.Today.AddDays(-1) ? $"Ontem {When:HH:mm}" : When.ToString("dd/MM HH:mm");
}

public sealed class CallItem : PersonItem
{
    public string Number { get; init; } = "";
    public string Kind { get; init; } = "";
    public DateTime When { get; init; }

    public bool IsMissed => Kind == "MISSED";
    public string KindLabel => Kind switch
    {
        "MISSED" => "Perdida",
        "DIALED" => "Feita",
        "RECEIVED" => "Recebida",
        _ => "",
    };
    public string KindGlyph => Kind == "DIALED" ? "" : "";
    public string TimeLabel => When == DateTime.MinValue ? "" : TimeFormat.Relative(When);
}

public sealed class ContactItem : PersonItem
{
    public List<(string Number, string Kind)> Phones { get; init; } = new();
    public List<string> Emails { get; init; } = new();
    public string SearchKey { get; init; } = "";

    public string PrimaryNumber => Phones.Count > 0 ? Phones[0].Number : "";
    public string Detail => Phones.Count > 0
        ? (Phones[0].Kind.Length > 0 ? $"{Phones[0].Kind} · {Phones[0].Number}" : Phones[0].Number)
        : Emails.FirstOrDefault() ?? "";
    public string FirstLetter => Name.Length > 0 && char.IsLetter(TextMatch.Normalize(Name)[0])
        ? char.ToUpperInvariant(TextMatch.Normalize(Name)[0]).ToString()
        : "#";
}

public static class IPhoneModels
{
    private static readonly Dictionary<string, string> Names = new()
    {
        ["iPhone12,1"] = "iPhone 11", ["iPhone12,3"] = "iPhone 11 Pro", ["iPhone12,5"] = "iPhone 11 Pro Max",
        ["iPhone12,8"] = "iPhone SE (2ª geração)",
        ["iPhone13,1"] = "iPhone 12 mini", ["iPhone13,2"] = "iPhone 12", ["iPhone13,3"] = "iPhone 12 Pro", ["iPhone13,4"] = "iPhone 12 Pro Max",
        ["iPhone14,4"] = "iPhone 13 mini", ["iPhone14,5"] = "iPhone 13", ["iPhone14,2"] = "iPhone 13 Pro", ["iPhone14,3"] = "iPhone 13 Pro Max",
        ["iPhone14,6"] = "iPhone SE (3ª geração)", ["iPhone14,7"] = "iPhone 14", ["iPhone14,8"] = "iPhone 14 Plus",
        ["iPhone15,2"] = "iPhone 14 Pro", ["iPhone15,3"] = "iPhone 14 Pro Max", ["iPhone15,4"] = "iPhone 15", ["iPhone15,5"] = "iPhone 15 Plus",
        ["iPhone16,1"] = "iPhone 15 Pro", ["iPhone16,2"] = "iPhone 15 Pro Max",
        ["iPhone17,1"] = "iPhone 16 Pro", ["iPhone17,2"] = "iPhone 16 Pro Max", ["iPhone17,3"] = "iPhone 16", ["iPhone17,4"] = "iPhone 16 Plus",
        ["iPhone17,5"] = "iPhone 16e",
    };

    public static string? Friendly(string? identifier)
    {
        if (string.IsNullOrWhiteSpace(identifier)) return null;
        return Names.TryGetValue(identifier, out var name) ? name : null;
    }
}
