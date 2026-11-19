using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Native.Bluetooth;
using Native.Core;
using Native.Models;
using Native.Protocol;
using Native.Services;

namespace Native.ViewModels;

public enum AppTab { Notifications, Messages, Phone, Files, Settings }

public enum StepState { Pending, Current, Done }

/// <summary>Um passo do guia de conexão.</summary>
public sealed class SetupStep : ObservableObject
{
    public string Number { get; init; } = "";
    public string Title { get; init; } = "";

    private string _detail = "";
    public string Detail { get => _detail; set => Set(ref _detail, value); }

    private StepState _state;
    public StepState State
    {
        get => _state;
        set { if (Set(ref _state, value)) { Raise(nameof(IsDone)); Raise(nameof(IsCurrent)); Raise(nameof(ShowAction)); } }
    }

    public bool IsDone => State == StepState.Done;
    public bool IsCurrent => State == StepState.Current;

    public string ActionLabel { get; init; } = "";
    public ICommand? Action { get; init; }
    public bool ShowAction => IsCurrent && Action is not null && ActionLabel.Length > 0;
}

/// <summary>Estado da janela principal: iPhone, notificações, mídia, abas, ajustes e espelhamento.</summary>
public sealed class MainViewModel : ObservableObject
{
    private readonly AncsClient _client;
    private readonly AppCatalog _catalog;
    private readonly AppSettings _settings;
    private readonly PeopleDirectory _people;
    private readonly DispatcherTimer _clock;
    private readonly DispatcherTimer _mediaClock;
    private readonly AppInfo _systemApp;
    private Process? _mirror;
    private NowPlaying? _nowPlaying;
    private uint _syntheticUid = uint.MaxValue;
    private LinkState _state = LinkState.Searching;

    public MainViewModel(AncsClient client, AppCatalog catalog, AppSettings settings,
        MessageService messages, ContactService contacts, CallService calls)
    {
        _client = client;
        _catalog = catalog;
        _settings = settings;
        _people = new PeopleDirectory(contacts);

        Messages = new MessagesViewModel(messages, _people);
        Phone = new PhoneViewModel(calls, _people);
        Phone.OpenConversation = async (name, address) =>
        {
            SelectedTab = AppTab.Messages;
            await Messages.OpenWithAsync(name, address);
        };

        _systemApp = new AppInfo("native.sistema", "iPhone") { FallbackBrush = AppInfo.BrushFor("sistema", "#30D158") };
        Files = new FilesViewModel(settings, RaiseSystemBanner);

        _client.StateChanged += (s, m) => Ui(() => OnState(s, m));
        _client.NotificationReceived += n => Ui(() => OnNotification(n));
        _client.NotificationRemoved += uid => Ui(() => OnRemoved(uid));
        _client.BatteryChanged += level => Ui(() => OnBattery(level));
        _client.NowPlayingChanged += np => Ui(() => OnNowPlaying(np));
        _client.MediaCommandsChanged += cmds => Ui(() => OnMediaCommands(cmds));

        _catalog.PermissionChanged += OnPermissionChanged;
        _catalog.Apps.CollectionChanged += (_, _) => Raise(nameof(HasNoApps));
        contacts.ContactsChanged += () => Ui(() => Raise(nameof(ContactsSummary)));

        PhoneNotification.ReplySender = SendReplyAsync;
        PhoneNotification.ExternalOpener = OpenInDesktopApp;
        PhoneNotification.CallBack = n => { SelectedTab = AppTab.Phone; _ = Phone.CallAsync(n.Title); };

        ToggleGroupCommand = new RelayCommand(p => { if (NotificationGroup.Stacking && p is NotificationGroup g && g.Items.Count > 1) g.IsExpanded = !g.IsExpanded; });
        DismissGroupCommand = new RelayCommand(p => { if (p is NotificationGroup g) DismissGroup(g); });
        ClearAllCommand = new RelayCommand(() => { foreach (var g in Groups.ToList()) DismissGroup(g); });
        MirrorCommand = new RelayCommand(ToggleMirror);
        CloseSheetCommand = new RelayCommand(() => IsSheetOpen = false);
        LocateUxPlayCommand = new RelayCommand(LocateUxPlay);
        CopyCodeCommand = new RelayCommand(p => { if (p is PhoneNotification n) CopyCode(n); });
        ToggleDoNotDisturbCommand = new RelayCommand(() => DoNotDisturb = !DoNotDisturb);
        ToggleKeepOnTopCommand = new RelayCommand(() => KeepOnTop = !KeepOnTop);
        ToggleMiniCommand = new RelayCommand(() => IsMiniMode = !IsMiniMode);
        ToggleMiniPinCommand = new RelayCommand(() => MiniPinned = !MiniPinned);
        NotificationGroup.Stacking = settings.StackNotifications;
        ToggleSettingsCommand = new RelayCommand(() => SelectedTab = SelectedTab == AppTab.Settings ? _tabBeforeSettings : AppTab.Settings);
        PlayPauseCommand = new RelayCommand(() => SendMedia(MediaCommand.TogglePlayPause));
        NextTrackCommand = new RelayCommand(() => SendMedia(MediaCommand.NextTrack));
        PreviousTrackCommand = new RelayCommand(() => SendMedia(MediaCommand.PreviousTrack));
        SkipForwardCommand = new RelayCommand(() => SendMedia(MediaCommand.SkipForward));
        SkipBackwardCommand = new RelayCommand(() => SendMedia(MediaCommand.SkipBackward));
        VolumeUpCommand = new RelayCommand(VolumeUp);
        VolumeDownCommand = new RelayCommand(VolumeDown);
        TogglePipCommand = new RelayCommand(() => IsPipOpen = !IsPipOpen);
        SelectTabCommand = new RelayCommand(p => { if (p is string name && Enum.TryParse<AppTab>(name, out var tab)) SelectedTab = tab; });
        RetryCommand = new RelayCommand(() => _client.RequestReconnect("pedido pelo usuário"));
        SkipSetupCommand = new RelayCommand(() => { _setupDismissed = true; Raise(nameof(ShowSetup)); });
        ShowSetupCommand = new RelayCommand(() => { _setupDismissed = false; _forceSetup = true; Raise(nameof(ShowSetup)); });
        OpenLogsCommand = new RelayCommand(() => TryOpen(Log.Folder));
        CopyDiagnosticsCommand = new RelayCommand(CopyDiagnostics);
        ClearContactsCommand = new RelayCommand(() => { contacts.ClearCache(); Raise(nameof(ContactsSummary)); });

        Steps = new ObservableCollection<SetupStep>
        {
            new() { Number = "1", Title = "Ligar o Bluetooth do PC", ActionLabel = "Ligar Bluetooth",
                    Action = new RelayCommand(async () => { if (!await AncsClient.TurnOnBluetoothAsync()) TryOpen("ms-settings:bluetooth"); _client.RequestReconnect("Bluetooth"); }) },
            new() { Number = "2", Title = "Parear o iPhone com este PC", ActionLabel = "Abrir Bluetooth do Windows",
                    Action = new RelayCommand(() => TryOpen("ms-settings:bluetooth")) },
            new() { Number = "3", Title = "Liberar as notificações no iPhone", ActionLabel = "Já liberei, tentar de novo",
                    Action = RetryCommand },
            new() { Number = "4", Title = "Fechar o Vincular ao Celular", ActionLabel = "Fechar e tentar de novo",
                    Action = new RelayCommand(() => { PhoneLinkGuard.Stop(); _client.RequestReconnect("Vincular ao Celular fechado"); }) },
        };
        UpdateSteps();

        _clock = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _clock.Tick += (_, _) =>
        {
            var before = _charge.State;
            if (_charge.Tick(DateTime.Now) != before) RaiseCharge();
        };
        _clock.Tick += (_, _) => { foreach (var g in Groups) g.RefreshTimes(); };
        _clock.Start();

        _mediaClock = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _mediaClock.Tick += (_, _) => RaiseMediaProgress();
    }

    public MessagesViewModel Messages { get; }
    public FilesViewModel Files { get; }
    public PhoneViewModel Phone { get; }

    // ------------------------------------------------------------ coleções

    public ObservableCollection<NotificationGroup> Groups { get; } = new();
    public ObservableCollection<AppInfo> Apps => _catalog.Apps;
    public ObservableCollection<SetupStep> Steps { get; }

    public bool HasNotifications => Groups.Count > 0;
    public bool IsEmpty => Groups.Count == 0;
    public bool HasNoApps => Apps.Count == 0;

    public event Action<PhoneNotification>? BannerRequested;
    public event Action<uint>? NotificationGone;

    // ------------------------------------------------------------ estado do iPhone

    private string _deviceName = "iPhone";
    public string DeviceName { get => _deviceName; private set => Set(ref _deviceName, value); }

    private string? _modelName;
    public string? ModelName
    {
        get => _modelName;
        private set { if (Set(ref _modelName, value)) Raise(nameof(HasModel)); }
    }
    public bool HasModel => !string.IsNullOrEmpty(ModelName);

    private string _statusText = "Procurando iPhone…";
    public string StatusText { get => _statusText; private set => Set(ref _statusText, value); }

    /// <summary>"ok", "busy" ou "problem": a interface escolhe a cor.</summary>
    private string _statusKind = "busy";
    public string StatusKind { get => _statusKind; private set => Set(ref _statusKind, value); }

    private bool _isConnected;
    public bool IsConnected
    {
        get => _isConnected;
        private set { if (Set(ref _isConnected, value)) Raise(nameof(EmptyHint)); }
    }

    public string EmptyHint => IsConnected
        ? "Quando chegar algo no iPhone, aparece aqui."
        : "Assim que o iPhone conectar, as notificações aparecem aqui.";

    private int? _batteryLevel;
    public int? BatteryLevel
    {
        get => _batteryLevel;
        private set
        {
            if (!Set(ref _batteryLevel, value)) return;
            Raise(nameof(HasBattery));
            Raise(nameof(BatteryText));
            Raise(nameof(BatteryFillWidth));
            Raise(nameof(BatteryLow));
        }
    }

    public bool HasBattery => BatteryLevel.HasValue;

    private readonly ChargeTracker _charge = new();
    public bool IsCharging => _charge.State == ChargeState.Charging;
    public bool IsFullyCharged => _charge.State == ChargeState.Full;
    public bool ShowChargeBolt => IsCharging || IsFullyCharged;
    public string BatteryTooltip => _charge.State switch
    {
        ChargeState.Charging => "Carregando (o Native percebe pela bateria subindo)",
        ChargeState.Full => "Carregado",
        _ => "Bateria do iPhone",
    };

    private void RaiseCharge()
    {
        Raise(nameof(IsCharging));
        Raise(nameof(IsFullyCharged));
        Raise(nameof(ShowChargeBolt));
        Raise(nameof(BatteryTooltip));
        Raise(nameof(BatteryText));
    }
    public string BatteryText => BatteryLevel.HasValue ? $"{BatteryLevel}%" : "";
    public double BatteryFillWidth => Math.Max(1.5, (BatteryLevel ?? 0) / 100.0 * 16.5);
    public bool BatteryLow => BatteryLevel <= 20;

    // ------------------------------------------------------------ guia de conexão

    private bool _setupDismissed;
    private bool _forceSetup;

    private static bool IsProblem(LinkState s) =>
        s is LinkState.NoBluetooth or LinkState.BluetoothOff or LinkState.NotPaired or LinkState.AccessDenied or LinkState.Busy;

    public bool ShowSetup => !IsConnected && !_setupDismissed && (_forceSetup || !_settings.SetupCompleted || IsProblem(_state));

    private void UpdateSteps()
    {
        int current = _state switch
        {
            LinkState.NoBluetooth or LinkState.BluetoothOff => 0,
            LinkState.NotPaired => 1,
            LinkState.AccessDenied => 2,
            LinkState.Busy => 3,
            LinkState.Connected => 4,
            _ => -1,
        };

        Steps[0].Detail = _state == LinkState.NoBluetooth
            ? "Este PC não tem Bluetooth. Um adaptador USB Bluetooth 4.0 ou mais novo resolve."
            : "O Native conversa com o iPhone por Bluetooth, sem internet e sem cabo.";
        Steps[1].Detail = "No iPhone, abra Ajustes > Bluetooth e deixe a tela aberta. No PC, clique em Adicionar dispositivo > Bluetooth e escolha o iPhone. Confirme o código nos dois.";
        Steps[2].Detail = "No iPhone: Ajustes > Bluetooth > toque no (i) ao lado deste PC e ative \"Compartilhar Notificações do Sistema\" e \"Sincronizar Contatos\".";
        Steps[3].Detail = "O Vincular ao Celular da Microsoft segura a conexão com o iPhone. O Native fecha ele sozinho, mas se voltar, desative em Configurações > Bluetooth > Dispositivos móveis.";

        for (int i = 0; i < Steps.Count; i++)
        {
            Steps[i].State = current < 0
                ? (i == 0 ? StepState.Done : StepState.Pending)
                : i < current ? StepState.Done : i == current ? StepState.Current : StepState.Pending;
        }
        if (current < 0)
        {
            // Procurando/conectando: os dois primeiros passos já estão ok.
            Steps[0].State = StepState.Done;
            Steps[1].State = StepState.Done;
        }
        Raise(nameof(ShowSetup));
        Raise(nameof(SetupHeadline));
    }

    public string SetupHeadline => _state switch
    {
        LinkState.Connected => "Tudo certo!",
        LinkState.Searching or LinkState.Connecting => "Procurando o iPhone…",
        LinkState.OutOfRange => "iPhone fora de alcance",
        _ => "Vamos conectar o seu iPhone",
    };

    // ------------------------------------------------------------ tocando agora

    public bool HasNowPlaying => _nowPlaying is { Title.Length: > 0 };
    public string TrackTitle => _nowPlaying?.Title ?? "";
    public string TrackArtist
    {
        get
        {
            if (_nowPlaying is null) return "";
            if (_nowPlaying.Artist.Length > 0 && _nowPlaying.Album.Length > 0) return $"{_nowPlaying.Artist} — {_nowPlaying.Album}";
            return _nowPlaying.Artist.Length > 0 ? _nowPlaying.Artist : _nowPlaying.Album;
        }
    }
    public string PlayerName => _nowPlaying?.Player ?? "";
    public bool IsPlaying => _nowPlaying?.IsPlaying ?? false;
    public string PlayPauseGlyph => IsPlaying ? "\uE769" : "\uE768";
    public bool HasPlayerName => PlayerName.Length > 0;

    // --- progresso e arrastar para pular

    private HashSet<MediaCommand> _mediaCommands = new();
    private bool _isScrubbing;
    private double _scrubValue;
    private DateTime _holdScrubUntil = DateTime.MinValue;

    private double CurrentElapsed => _nowPlaying?.ElapsedAt(DateTime.Now) ?? 0;
    private double Duration => _nowPlaying?.Duration ?? 0;
    private bool ShowingScrub => _isScrubbing || DateTime.Now < _holdScrubUntil;
    private double ShownElapsed => ShowingScrub && Duration > 0 ? _scrubValue * Duration : CurrentElapsed;

    /// <summary>Posição da música (0 a 1). Enquanto a pessoa arrasta, segue o dedo, não o iPhone.</summary>
    public double TrackProgress
    {
        get => ShowingScrub ? _scrubValue : Duration > 0 ? CurrentElapsed / Duration : 0;
        set
        {
            if (!_isScrubbing) return;
            _scrubValue = Math.Clamp(value, 0, 1);
            Raise(nameof(ElapsedText));
            Raise(nameof(RemainingText));
        }
    }

    public string ElapsedText => TimeFormat.Clock(ShownElapsed);
    public string RemainingText => Duration > 0 ? "-" + TimeFormat.Clock(Duration - ShownElapsed) : "";

    public bool CanSkipForward => _mediaCommands.Contains(MediaCommand.SkipForward);
    public bool CanSkipBackward => _mediaCommands.Contains(MediaCommand.SkipBackward);

    /// <summary>Dá para arrastar a barra? Só se o app aceita avançar/voltar (o iPhone não tem "ir para 1:23").</summary>
    public bool CanSeek => Duration > 0 && (CanSkipForward || CanSkipBackward);

    public string SeekHint => CanSeek
        ? "Arraste para pular (o iPhone avança de 15 em 15 segundos)"
        : $"O {(PlayerName.Length > 0 ? PlayerName : "app")} não deixa pular pelo Bluetooth. Use anterior/próxima.";

    public void BeginScrub()
    {
        if (!CanSeek) return;
        _isScrubbing = true;
        _scrubValue = TrackProgress;
    }

    public void CommitScrub()
    {
        if (!_isScrubbing) return;
        _isScrubbing = false;
        var target = _scrubValue * Duration;
        int steps = Protocol.MediaCommands.SkipStepsBetween(CurrentElapsed, target);
        _holdScrubUntil = DateTime.Now.AddSeconds(2.5);   // segura a barra até o iPhone confirmar
        _ = SendRepeatedAsync(steps > 0 ? MediaCommand.SkipForward : MediaCommand.SkipBackward, Math.Abs(steps), 140);
        RaiseMediaProgress();
    }

    // --- volume

    private bool _isAdjustingVolume;
    private double _volumeShown = -1;
    private DateTime _holdVolumeUntil = DateTime.MinValue;

    public bool HasVolume => _nowPlaying?.HasVolume ?? false;

    /// <summary>Volume do iPhone (0 a 1). Arrastar só manda os comandos ao soltar.</summary>
    public double Volume
    {
        get => _isAdjustingVolume || DateTime.Now < _holdVolumeUntil ? _volumeShown : Math.Max(0, _nowPlaying?.Volume ?? 0);
        set
        {
            _volumeShown = Math.Clamp(value, 0, 1);
            if (!_isAdjustingVolume) CommitVolume();     // clique direto na barra
            else Raise(nameof(VolumeGlyph));
        }
    }

    public string VolumeGlyph => Volume <= 0.001 ? "\uE74F" : Volume < 0.34 ? "\uE993" : Volume < 0.67 ? "\uE994" : "\uE995";

    public void BeginVolumeChange()
    {
        _isAdjustingVolume = true;
        _volumeShown = Volume;
    }

    public void CommitVolume()
    {
        _isAdjustingVolume = false;
        var current = Math.Max(0, _nowPlaying?.Volume ?? 0);
        int steps = Protocol.MediaCommands.VolumeStepsBetween(current, _volumeShown);
        _holdVolumeUntil = DateTime.Now.AddSeconds(2);
        _ = SendRepeatedAsync(steps > 0 ? MediaCommand.VolumeUp : MediaCommand.VolumeDown, Math.Abs(steps), 70);
        Raise(nameof(Volume));
        Raise(nameof(VolumeGlyph));
    }

    public void VolumeUp() => NudgeVolume(+1);
    public void VolumeDown() => NudgeVolume(-1);

    private void NudgeVolume(int direction)
    {
        SendMedia(direction > 0 ? MediaCommand.VolumeUp : MediaCommand.VolumeDown);
        if (!HasVolume) return;
        _volumeShown = Math.Clamp(Volume + direction / (double)Protocol.MediaCommands.VolumeSteps, 0, 1);
        _holdVolumeUntil = DateTime.Now.AddSeconds(1.5);
        Raise(nameof(Volume));
        Raise(nameof(VolumeGlyph));
    }

    private readonly System.Threading.SemaphoreSlim _mediaQueue = new(1, 1);

    private async Task SendRepeatedAsync(MediaCommand command, int times, int delayMs)
    {
        if (times <= 0) return;
        await _mediaQueue.WaitAsync();
        try
        {
            for (int i = 0; i < times; i++)
            {
                await _client.SendMediaCommandAsync(command);
                if (i < times - 1) await Task.Delay(delayMs);
            }
        }
        finally
        {
            _mediaQueue.Release();
        }
    }

    // --- mini player flutuante

    private bool _isPipOpen;
    public bool IsPipOpen { get => _isPipOpen; set => Set(ref _isPipOpen, value); }

    public double? PipLeft { get => _settings.PipLeft; set { _settings.PipLeft = value; _settings.Save(); } }
    public double? PipTop { get => _settings.PipTop; set { _settings.PipTop = value; _settings.Save(); } }

    // ------------------------------------------------------------ abas

    private AppTab _selectedTab = AppTab.Notifications;
    public AppTab SelectedTab
    {
        get => _selectedTab;
        set
        {
            var previous = _selectedTab;
            if (!Set(ref _selectedTab, value)) return;
            if (value == AppTab.Settings && previous != AppTab.Settings) _tabBeforeSettings = previous;
            Raise(nameof(IsNotificationsTab));
            Raise(nameof(IsMessagesTab));
            Raise(nameof(IsPhoneTab));
            Raise(nameof(IsFilesTab));
            Raise(nameof(IsSettingsTab));
            if (value == AppTab.Messages && IsConnected) _ = Messages.LoadAsync();
            if (value == AppTab.Phone && IsConnected) _ = Phone.LoadRecentsAsync();
        }
    }

    public bool IsNotificationsTab { get => SelectedTab == AppTab.Notifications; set { if (value) SelectedTab = AppTab.Notifications; } }
    public bool IsMessagesTab { get => SelectedTab == AppTab.Messages; set { if (value) SelectedTab = AppTab.Messages; } }
    public bool IsPhoneTab { get => SelectedTab == AppTab.Phone; set { if (value) SelectedTab = AppTab.Phone; } }
    public bool IsFilesTab { get => SelectedTab == AppTab.Files; set { if (value) SelectedTab = AppTab.Files; } }
    public bool IsSettingsTab { get => SelectedTab == AppTab.Settings; set { if (value) SelectedTab = AppTab.Settings; } }

    // ------------------------------------------------------------ ajustes

    public bool ShowBanners
    {
        get => _settings.ShowBanners;
        set { _settings.ShowBanners = value; _settings.Save(); Raise(); }
    }

    public bool PlaySound
    {
        get => _settings.PlaySound;
        set { _settings.PlaySound = value; _settings.Save(); Raise(); }
    }

    public bool ShowWhenPhoneSilenced
    {
        get => _settings.ShowWhenPhoneSilenced;
        set { _settings.ShowWhenPhoneSilenced = value; _settings.Save(); Raise(); }
    }

    /// <summary>Pilha de notificações por app. Vale para a janela e para o mini.</summary>
    public bool StackNotifications
    {
        get => _settings.StackNotifications;
        set
        {
            _settings.StackNotifications = value; _settings.Save();
            NotificationGroup.Stacking = value;
            foreach (var g in Groups) { if (!value) g.IsExpanded = false; g.Changed(); }
            Raise();
            RefreshRecent();
        }
    }

    /// <summary>Liquid Glass: superfícies translúcidas com brilho de vidro.</summary>
    public bool GlassMode
    {
        get => _settings.Glass;
        set
        {
            _settings.Glass = value; _settings.Save(); ThemeManager.SetGlass(value);
            Raise(); Raise(nameof(GlassNeedsRestart));
        }
    }

    /// <summary>Quanto o fundo cobre: menos = dá para ver mais o que está atrás (série, vídeo).</summary>
    public double GlassOpacity
    {
        get => _settings.GlassOpacity;
        set
        {
            value = Math.Clamp(value, 0.25, 1);
            if (Math.Abs(_settings.GlassOpacity - value) < 0.0005) return;
            _settings.GlassOpacity = value;
            ThemeManager.SetGlassOpacity(value);
            // grava só quando o controle para de mexer (gravar a cada pixel arrastado engasgava)
            _glassSave ??= new System.Windows.Threading.DispatcherTimer(TimeSpan.FromMilliseconds(400),
                System.Windows.Threading.DispatcherPriority.Background, (_, _) => { _glassSave!.Stop(); _settings.Save(); },
                Application.Current.Dispatcher);
            _glassSave.Stop(); _glassSave.Start();
            Raise(); Raise(nameof(GlassOpacityText));
        }
    }

    /// <summary>Vidro fosco: desfoca o que está atrás (Windows 11).</summary>
    public bool GlassBlur
    {
        get => _settings.GlassBlur;
        set { _settings.GlassBlur = value; _settings.Save(); ThemeManager.SetGlassBlur(value); Raise(); }
    }

    public bool BlurSupported => WindowEffects.BlurSupported;

    public bool IsEngine1 { get => _settings.GlassEngine == 1; set { if (value) SetEngine(1); } }
    public bool IsEngine2 { get => _settings.GlassEngine == 2; set { if (value) SetEngine(2); } }
    public bool IsEngine3 { get => _settings.GlassEngine == 3; set { if (value) SetEngine(3); } }

    private void SetEngine(int engine)
    {
        _settings.GlassEngine = engine; _settings.Save();
        WindowEffects.BlurEngine = engine;
        ThemeManager.SetGlassBlur(GlassBlur);   // reaplica nas janelas
        Raise(nameof(IsEngine1)); Raise(nameof(IsEngine2)); Raise(nameof(IsEngine3));
    }

    /// <summary>O controle desliza em "transparência" (o contrário da opacidade).</summary>
    public double GlassTransparency
    {
        get => 1 - GlassOpacity;
        set => GlassOpacity = 1 - value;   // sem Raise aqui: a barra é quem manda enquanto arrasta
    }

    private System.Windows.Threading.DispatcherTimer? _glassSave;

    public string GlassOpacityText => $"{Math.Round((1 - GlassOpacity) * 100)}% transparente";

    /// <summary>A janela grande só vira vidro de verdade (vendo o que está atrás) depois de reabrir o Native.</summary>
    public bool GlassNeedsRestart => GlassMode != ThemeManager.WindowGlassAtStartup;

    public event Action? RestartRequested;
    public ICommand RestartCommand => _restartCommand ??= new RelayCommand(() => RestartRequested?.Invoke());
    private ICommand? _restartCommand;

    public bool AutoCopyCodes
    {
        get => _settings.AutoCopyCodes;
        set { _settings.AutoCopyCodes = value; _settings.Save(); Raise(); }
    }

    public bool SyncContacts
    {
        get => _settings.SyncContacts;
        set { _settings.SyncContacts = value; _settings.Save(); Raise(); }
    }

    public bool DoNotDisturb
    {
        get => _settings.DoNotDisturb;
        set
        {
            _settings.DoNotDisturb = value;
            _settings.Save();
            Raise();
            Raise(nameof(DoNotDisturbTooltip));
        }
    }

    public string DoNotDisturbTooltip => DoNotDisturb
        ? "Não Perturbe ativado: sem banners nem som (ligações ainda aparecem)"
        : "Ativar Não Perturbe";

    public bool AirPlayAlwaysOn
    {
        get => _settings.AirPlayAlwaysOn;
        set
        {
            _settings.AirPlayAlwaysOn = value;
            _settings.Save();
            Raise();
            if (value && !IsMirroring) StartAirPlayInBackground();
        }
    }

    public bool StartWithWindows
    {
        get => StartupRegistration.IsEnabled;
        set
        {
            try { StartupRegistration.Set(value); }
            catch (Exception ex) { Log.Warn("Inicialização", Log.FirstLine(ex.Message)); }
            Raise();
        }
    }

    public string[] ThemeOptions { get; } = { "Igual ao Windows", "Claro", "Escuro" };

    public int ThemeIndex
    {
        get => _settings.Theme switch { "claro" => 1, "escuro" => 2, _ => 0 };
        set
        {
            _settings.Theme = value switch { 1 => "claro", 2 => "escuro", _ => "sistema" };
            _settings.Save();
            ThemeManager.Apply(_settings.Theme);
            Raise();
        }
    }

    public bool IsThemeSystem { get => ThemeIndex == 0; set { if (value) SetTheme(0); } }
    public bool IsThemeLight { get => ThemeIndex == 1; set { if (value) SetTheme(1); } }
    public bool IsThemeDark { get => ThemeIndex == 2; set { if (value) SetTheme(2); } }

    private void SetTheme(int index)
    {
        ThemeIndex = index;
        Raise(nameof(IsThemeSystem));
        Raise(nameof(IsThemeLight));
        Raise(nameof(IsThemeDark));
    }

    public string UxPlayLabel =>
        File.Exists(_settings.UxPlayPath ?? "") ? "Configurado" :
        DetectUxPlay() is not null ? "Instalado" : "Não instalado";

    public string ContactsSummary => _people.Service.LastSync is { } when
        ? $"{_people.Service.Contacts.Count} contatos guardados neste PC"
        : "Nenhum contato guardado";

    public string VersionLabel => $"Native {typeof(MainViewModel).Assembly.GetName().Version?.ToString(3)}";

    // ------------------------------------------------------------ espelhamento e painel

    private bool _isMirroring;
    public bool IsMirroring
    {
        get => _isMirroring;
        private set { if (Set(ref _isMirroring, value)) Raise(nameof(MirrorButtonText)); }
    }

    public string MirrorButtonText => IsMirroring ? "Parar" : "Espelhar";

    private bool _isSheetOpen;
    public bool IsSheetOpen { get => _isSheetOpen; set => Set(ref _isSheetOpen, value); }

    private string _sheetTitle = "";
    public string SheetTitle { get => _sheetTitle; private set => Set(ref _sheetTitle, value); }

    private string _sheetBody = "";
    public string SheetBody { get => _sheetBody; private set => Set(ref _sheetBody, value); }

    private bool _sheetShowLocate;
    public bool SheetShowLocate { get => _sheetShowLocate; private set => Set(ref _sheetShowLocate, value); }

    // ------------------------------------------------------------ comandos

    public ICommand ToggleGroupCommand { get; }
    public ICommand DismissGroupCommand { get; }
    public ICommand ClearAllCommand { get; }
    public ICommand MirrorCommand { get; }
    public ICommand CloseSheetCommand { get; }
    public ICommand LocateUxPlayCommand { get; }
    public ICommand CopyCodeCommand { get; }
    public ICommand ToggleDoNotDisturbCommand { get; }
    public ICommand ToggleKeepOnTopCommand { get; }
    public ICommand ToggleSettingsCommand { get; }
    public ICommand ToggleMiniCommand { get; }
    public ICommand ToggleMiniPinCommand { get; }

    /// <summary>Alfinete do mini: preso por cima de tudo, ou janela normal que pode ficar atrás.</summary>
    public bool MiniPinned
    {
        get => _settings.MiniPinned;
        set
        {
            if (_settings.MiniPinned == value) return;
            _settings.MiniPinned = value;
            _settings.Save();
            Raise(nameof(MiniPinned));
            Raise(nameof(MiniPinTooltip));
        }
    }

    public string MiniPinTooltip => MiniPinned ? "Preso na frente de tudo (clique para soltar)" : "Prender na frente de tudo";

    /// <summary>Fixar: a janela fica por cima de tudo e não some ao clicar em outro programa.</summary>
    public bool KeepOnTop
    {
        get => _settings.KeepOnTop;
        set
        {
            if (_settings.KeepOnTop == value) return;
            _settings.KeepOnTop = value;
            _settings.Save();
            Raise(nameof(KeepOnTop));
            Raise(nameof(KeepOnTopTooltip));
        }
    }

    public string KeepOnTopTooltip => KeepOnTop ? "Fixada na frente de tudo (clique para soltar)" : "Fixar: manter na frente de tudo";

    private AppTab _tabBeforeSettings = AppTab.Notifications;
    public ICommand PlayPauseCommand { get; }
    public ICommand NextTrackCommand { get; }
    public ICommand PreviousTrackCommand { get; }
    public ICommand SkipForwardCommand { get; }
    public ICommand SkipBackwardCommand { get; }
    public ICommand VolumeUpCommand { get; }
    public ICommand VolumeDownCommand { get; }
    public ICommand TogglePipCommand { get; }
    public ICommand SelectTabCommand { get; }
    public ICommand RetryCommand { get; }
    public ICommand SkipSetupCommand { get; }
    public ICommand ShowSetupCommand { get; }
    public ICommand OpenLogsCommand { get; }
    public ICommand CopyDiagnosticsCommand { get; }
    public ICommand ClearContactsCommand { get; }

    public void PerformAction(PhoneNotification n, bool positive) =>
        _ = _client.PerformActionAsync(n.Uid, positive);

    public void CopyText(PhoneNotification n) => ClipboardHelper.TrySetText(n.FullText);

    public void CopyCode(PhoneNotification n)
    {
        if (n.Code is not null && ClipboardHelper.TrySetText(n.Code)) n.CodeCopied = true;
    }

    public void MuteApp(AppInfo app) => app.IsAllowed = false;

    public void DismissItem(PhoneNotification n)
    {
        if (Find(n.Uid) is { } f) Remove(f.Group, f.Item, tellPhone: true);
    }

    /// <summary>Banner e som devem aparecer agora?</summary>
    public bool ShouldAlert(PhoneNotification n) => !DoNotDisturb || n.IsCall;

    public void RequestReconnect(string reason) => _client.RequestReconnect(reason);

    // ------------------------------------------------------------ eventos do iPhone

    private void OnState(LinkState state, string message)
    {
        bool wasConnected = IsConnected;
        _state = state;
        StatusText = message;
        StatusKind = state switch
        {
            LinkState.Connected => "ok",
            LinkState.Searching or LinkState.Connecting => "busy",
            _ => "problem",
        };
        IsConnected = state == LinkState.Connected;

        if (IsConnected)
        {
            DeviceName = _client.DeviceName;
            ModelName = IPhoneModels.Friendly(_client.ModelIdentifier);
            _forceSetup = false;
            if (!_settings.SetupCompleted)
            {
                _settings.SetupCompleted = true;
                _settings.Save();
            }
            if (!wasConnected)
            {
                // Ao conectar, o iPhone reenvia tudo o que está na Central dele: começamos do zero.
                Groups.Clear();
                RaiseCounts();
                _ = AfterConnectAsync();
            }
        }
        else
        {
            BatteryLevel = null;
            _charge.Reset();
            RaiseCharge();
        }
        UpdateSteps();
    }

    /// <summary>Depois de conectar: agenda (uma vez por dia) e a aba aberta.</summary>
    private async Task AfterConnectAsync()
    {
        if (_demo) return;
        await Task.Delay(TimeSpan.FromSeconds(4));   // deixa o LE terminar de assentar
        if (!IsConnected) return;

        if (_settings.SyncContacts && (_people.Service.LastSync is null || (DateTime.Now - _people.Service.LastSync.Value).TotalHours > 20))
        {
            try { await _people.Service.SyncContactsAsync(); }
            catch (ClassicException ex) { Log.Info("PBAP", $"Agenda não sincronizada agora: {ex.Message}"); }
        }

        if (SelectedTab == AppTab.Messages) await Messages.LoadAsync(force: true);
        if (SelectedTab == AppTab.Phone) await Phone.LoadRecentsAsync(force: true);
    }

    private void OnBattery(int level)
    {
        int? previous = BatteryLevel;
        var before = _charge.State;
        var now = _charge.Update(level, DateTime.Now);
        BatteryLevel = level;
        if (now != before) RaiseCharge();
        if (previous is null) return;

        if (before != ChargeState.Charging && now == ChargeState.Charging)
            Log.Info("Bateria", $"carregando ({previous}% → {level}%)");
        if (before == ChargeState.Charging && now == ChargeState.Full)
            RaiseSystemBanner("iPhone carregado", $"O {DeviceName} chegou a 100%.");

        foreach (var threshold in new[] { 20, 10 })
        {
            if (previous > threshold && level <= threshold)
            {
                RaiseSystemBanner("Bateria fraca", $"Restam {level}% de bateria no {DeviceName}.");
                break;
            }
        }
    }

    private void OnNowPlaying(NowPlaying? np)
    {
        // Chegou posição nova do iPhone depois de um pulo: solta a barra.
        if (_nowPlaying is not null && np is not null && Math.Abs(np.Elapsed - _nowPlaying.Elapsed) > 0.01) _holdScrubUntil = DateTime.MinValue;
        if (np is not null && _nowPlaying is not null && np.Volume != _nowPlaying.Volume) _holdVolumeUntil = DateTime.MinValue;
        if (np is null) _mediaCommands = new HashSet<MediaCommand>();

        _nowPlaying = np;
        Raise(nameof(HasNowPlaying));
        Raise(nameof(TrackTitle));
        Raise(nameof(TrackArtist));
        Raise(nameof(PlayerName));
        Raise(nameof(HasPlayerName));
        Raise(nameof(IsPlaying));
        Raise(nameof(PlayPauseGlyph));
        Raise(nameof(HasVolume));
        Raise(nameof(CanSeek));
        Raise(nameof(SeekHint));
        if (!_isAdjustingVolume)
        {
            Raise(nameof(Volume));
            Raise(nameof(VolumeGlyph));
        }
        RaiseMediaProgress();

        if (np is { IsPlaying: true }) _mediaClock.Start();
        else _mediaClock.Stop();
    }

    private void OnMediaCommands(IReadOnlyCollection<MediaCommand> commands)
    {
        _mediaCommands = new HashSet<MediaCommand>(commands);
        Raise(nameof(CanSkipForward));
        Raise(nameof(CanSkipBackward));
        Raise(nameof(CanSeek));
        Raise(nameof(SeekHint));
    }

    private void RaiseMediaProgress()
    {
        if (_isScrubbing) return;
        Raise(nameof(TrackProgress));
        Raise(nameof(ElapsedText));
        Raise(nameof(RemainingText));
    }

    private void OnNotification(AncsNotification n)
    {
        var app = _catalog.Get(n.AppId);
        if (!app.IsAllowed) return;

        var found = Find(n.Uid);
        if (found is { } f)
        {
            f.Item.Apply(n);
            f.Group.Changed();
            Reposition(f.Group);
            return;
        }

        var item = new PhoneNotification(n, app);
        if (n.AppId is "com.apple.MobileSMS" or "com.apple.mobilephone" or "com.apple.facetime" or "com.apple.InCallService")
            item.ContactPhoto = _people.PhotoFor(n.Title);

        var group = Groups.FirstOrDefault(g => g.App == app);
        if (group is null)
        {
            group = new NotificationGroup(app);
            Groups.Insert(0, group);
        }

        int i = 0;
        while (i < group.Items.Count && group.Items[i].ReceivedAt >= item.ReceivedAt) i++;
        group.Items.Insert(i, item);

        Reposition(group);
        RaiseCounts();

        // Com Foco/Não Perturbe ligado no iPhone, ele manda a notificação marcada como silenciosa.
        if (n.IsPreExisting || (n.IsSilent && !ShowWhenPhoneSilenced)) return;

        // Chegou SMS/iMessage: a aba Mensagens se atualiza sozinha.
        if (n.AppId == "com.apple.MobileSMS" && SelectedTab == AppTab.Messages) _ = Messages.LoadAsync(force: true);

        if (item.HasCode && AutoCopyCodes) CopyCode(item);
        BannerRequested?.Invoke(item);
    }

    private void OnRemoved(uint uid)
    {
        if (Find(uid) is { } f) Remove(f.Group, f.Item, tellPhone: false);
    }

    private void OnPermissionChanged(AppInfo app)
    {
        if (app.IsAllowed) return;
        var group = Groups.FirstOrDefault(g => g.App == app);
        if (group is null) return;
        foreach (var item in group.Items.ToList()) NotificationGone?.Invoke(item.Uid);
        Groups.Remove(group);
        RaiseCounts();
    }

    private void RaiseSystemBanner(string title, string message)
    {
        var n = new AncsNotification(_syntheticUid--, AncsCategory.Other,
            IsSilent: false, IsImportant: true, IsPreExisting: false,
            HasPositiveAction: false, HasNegativeAction: false,
            AppId: _systemApp.AppId, Title: title, Subtitle: "", Message: message, ReceivedAt: DateTime.Now);
        BannerRequested?.Invoke(new PhoneNotification(n, _systemApp));
    }

    // ------------------------------------------------------------ ações

    private void DismissGroup(NotificationGroup group)
    {
        foreach (var item in group.Items.ToList()) Remove(group, item, tellPhone: true);
    }

    private void Remove(NotificationGroup group, PhoneNotification item, bool tellPhone)
    {
        group.Items.Remove(item);
        if (group.Items.Count == 0) Groups.Remove(group);
        RaiseCounts();
        NotificationGone?.Invoke(item.Uid);

        // Limpar no PC também limpa no iPhone (ação negativa = "Limpar"). Em ligações seria "Recusar", então não.
        if (tellPhone && item.HasNegativeAction && !item.IsCall)
            _ = _client.PerformActionAsync(item.Uid, positive: false);
    }

    private (NotificationGroup Group, PhoneNotification Item)? Find(uint uid)
    {
        foreach (var g in Groups)
            foreach (var n in g.Items)
                if (n.Uid == uid) return (g, n);
        return null;
    }

    private void Reposition(NotificationGroup group)
    {
        int current = Groups.IndexOf(group);
        if (current < 0) return;
        var others = Groups.Where(g => g != group).ToList();
        int target = others.FindIndex(g => g.LatestTime < group.LatestTime);
        if (target < 0) target = others.Count;
        if (target != current) Groups.Move(current, target);
        RefreshRecent();
    }

    private void RaiseCounts()
    {
        Raise(nameof(HasNotifications));
        Raise(nameof(IsEmpty));
        RefreshRecent();
    }

    // ------------------------------------------------------------ mini Native

    /// <summary>As notificações mais novas de todos os apps, para o mini Native.</summary>
    public ObservableCollection<PhoneNotification> RecentNotifications { get; } = new();
    /// <summary>No modo pilha: os apps mais recentes, cada um como uma pilha.</summary>
    public ObservableCollection<NotificationGroup> RecentGroups { get; } = new();
    public int NotificationCount => Groups.Sum(g => g.Items.Count);
    private int ShownInMini => StackNotifications ? RecentGroups.Sum(g => g.Items.Count) : RecentNotifications.Count;
    public string MoreNotificationsText => NotificationCount > ShownInMini
        ? $"+ {NotificationCount - ShownInMini} no Native" : "";

    private void RefreshRecent()
    {
        var top = Groups.SelectMany(g => g.Items).OrderByDescending(n => n.ReceivedAt).Take(4).ToList();
        if (!top.SequenceEqual(RecentNotifications))
        {
            RecentNotifications.Clear();
            foreach (var n in top) RecentNotifications.Add(n);
        }
        var groups = Groups.Take(3).ToList();
        if (!groups.SequenceEqual(RecentGroups))
        {
            RecentGroups.Clear();
            foreach (var g in groups) RecentGroups.Add(g);
        }
        Raise(nameof(NotificationCount));
        Raise(nameof(MoreNotificationsText));
    }

    private bool _isMiniMode;
    /// <summary>Mini Native: uma janelinha fixa com o essencial do iPhone, no lugar da janela grande.</summary>
    public bool IsMiniMode
    {
        get => _isMiniMode;
        set
        {
            if (!Set(ref _isMiniMode, value)) return;
            if (!_settings.ReadOnly) { _settings.MiniMode = value; _settings.Save(); }
        }
    }

    public bool StartInMiniMode => _settings.MiniMode;
    public double? MiniLeft { get => _settings.MiniLeft; set { _settings.MiniLeft = value; _settings.Save(); } }
    public double? MiniTop { get => _settings.MiniTop; set { _settings.MiniTop = value; _settings.Save(); } }

    private void SendMedia(MediaCommand command) => _ = _client.SendMediaCommandAsync(command);

    // ------------------------------------------------------------ responder

    /// <summary>SMS e iMessage: resposta direta pelo Bluetooth clássico (MAP), sem tocar no iPhone.</summary>
    private async Task<string?> SendReplyAsync(PhoneNotification n, string text)
    {
        try
        {
            await Messages.ReplyToNotificationAsync(n.Title, text);
            return null;
        }
        catch (ClassicException ex)
        {
            return ex.Message;
        }
        catch (Exception ex)
        {
            Log.Error("Resposta", "Falha ao responder", ex);
            return "Não foi possível enviar.";
        }
    }

    /// <summary>
    /// WhatsApp, Instagram e afins não respondem pelo Bluetooth. O melhor caminho é abrir o app no PC
    /// já com o nome do contato copiado, para colar na busca.
    /// </summary>
    private void OpenInDesktopApp(PhoneNotification n)
    {
        var (uri, fallback) = n.ExternalAppName switch
        {
            "WhatsApp" => ("whatsapp:", "https://web.whatsapp.com"),
            "Telegram" => ("tg:", "https://web.telegram.org"),
            "Instagram" => ("https://www.instagram.com/direct/inbox/", (string?)null),
            "Messenger" => ("https://www.messenger.com", (string?)null),
            _ => ("", (string?)null),
        };
        if (uri.Length == 0) return;

        bool copied = !string.IsNullOrWhiteSpace(n.Title) && ClipboardHelper.TrySetText(n.Title);

        if (!TryOpen(uri) && fallback is not null) TryOpen(fallback);
        n.ShowHint(copied
            ? $"\"{n.Title}\" copiado. Cole na busca do {n.ExternalAppName} (Ctrl + V)."
            : $"{n.ExternalAppName} aberto.");
    }

    private static bool TryOpen(string target)
    {
        try
        {
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
            return true;
        }
        catch (Exception ex)
        {
            Log.Warn("Abrir", $"{target}: {Log.FirstLine(ex.Message)}");
            return false;
        }
    }

    private void CopyDiagnostics()
    {
        var text =
            $"{VersionLabel}\n" +
            $"Windows {Environment.OSVersion.Version} ({(Environment.Is64BitOperatingSystem ? "64" : "32")} bits)\n" +
            $"Estado: {_state} — {StatusText}\n" +
            $"iPhone: {DeviceName} {_client.ModelIdentifier}\n" +
            $"Tema: {_settings.Theme}\n\n" +
            Log.Tail(150);
        if (ClipboardHelper.TrySetText(text))
            ShowSheet("Diagnóstico copiado", "As informações técnicas e o registro de hoje estão na área de transferência. Nada do conteúdo das suas mensagens vai junto.", showLocate: false);
    }

    // ------------------------------------------------------------ espelhamento

    private void ToggleMirror()
    {
        if (IsMirroring)
        {
            StopMirror();
            return;
        }

        var path = ResolveUxPlayPath();
        if (path is null)
        {
            ShowSheet(
                "Espelhar a tela do iPhone",
                "O espelhamento usa o AirPlay, pelo Wi-Fi: o iPhone e o PC precisam estar na mesma rede. " +
                "Instale o uxplay-windows (receptor AirPlay gratuito) e tente de novo. Se instalou em outra pasta, indique onde está o programa.",
                showLocate: true);
            return;
        }

        StartMirror(path, showInstructions: true);
    }

    /// <summary>Deixa o receptor AirPlay ligado em segundo plano, como um Mac sempre pronto para receber.</summary>
    public void StartAirPlayInBackground()
    {
        if (IsMirroring) return;
        var path = ResolveUxPlayPath();
        if (path is not null) StartMirror(path, showInstructions: false);
    }

    private string? ResolveUxPlayPath()
    {
        var path = _settings.UxPlayPath;
        if (!string.IsNullOrEmpty(path) && File.Exists(path)) return path;

        path = DetectUxPlay();
        if (path is null) return null;
        _settings.UxPlayPath = path;
        _settings.Save();
        Raise(nameof(UxPlayLabel));
        return path;
    }

    private static string? DetectUxPlay()
    {
        var candidates = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "uxplay-windows", "uxplay-windows.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "uxplay-windows", "uxplay-windows.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "UxPlay", "uxplay.exe"),
        };
        return candidates.FirstOrDefault(File.Exists);
    }

    /// <summary>O uxplay-windows lê os parâmetros de um arquivo; definimos o nome que aparece no iPhone.</summary>
    private static void EnsureUxPlayArguments()
    {
        try
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "leapbtw", "uxplay-windows");
            var file = Path.Combine(dir, "arguments.txt");
            // O uxplay-windows não expande %COMPUTERNAME%: gravamos o nome já resolvido.
            var desired = $"-n {Environment.MachineName} -nh";
            if (File.Exists(file) && File.ReadAllText(file).Trim() == desired) return;
            Directory.CreateDirectory(dir);
            File.WriteAllText(file, desired);
        }
        catch (Exception ex)
        {
            Log.Warn("AirPlay", $"Não gravei os parâmetros: {Log.FirstLine(ex.Message)}");
        }
    }

    private void StartMirror(string path, bool showInstructions)
    {
        try
        {
            bool isWindowsApp = Path.GetFileName(path).Equals("uxplay-windows.exe", StringComparison.OrdinalIgnoreCase);
            if (isWindowsApp) EnsureUxPlayArguments();

            var psi = new ProcessStartInfo(path)
            {
                Arguments = isWindowsApp ? "" : $"-n \"{Environment.MachineName}\" -nh",
                UseShellExecute = false,
                CreateNoWindow = !isWindowsApp,
                WorkingDirectory = Path.GetDirectoryName(path) ?? "",
            };
            var process = Process.Start(psi) ?? throw new InvalidOperationException("O receptor AirPlay não abriu.");
            process.EnableRaisingEvents = true;
            process.Exited += (_, _) => Ui(() => { IsMirroring = false; _mirror = null; });
            _mirror = process;
            IsMirroring = true;
            Log.Info("AirPlay", "Receptor ligado");

            if (showInstructions)
            {
                ShowSheet(
                    "Pronto para espelhar",
                    $"No iPhone, abra a Central de Controle, toque em Espelhamento de Tela e escolha \"{Environment.MachineName}\". Se a imagem congelar no começo, pare e conecte de novo.",
                    showLocate: false);
            }
        }
        catch (Exception ex)
        {
            Log.Error("AirPlay", "Falha ao iniciar o receptor", ex);
            if (showInstructions) ShowSheet("Não foi possível iniciar o espelhamento", Log.FirstLine(ex.Message), showLocate: true);
        }
    }

    public void StopMirror()
    {
        try
        {
            if (_mirror is { HasExited: false }) _mirror.Kill(true);
        }
        catch { }
        _mirror = null;
        IsMirroring = false;
    }

    private void LocateUxPlay()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Localizar o receptor AirPlay",
            Filter = "UxPlay (uxplay-windows.exe, uxplay.exe)|uxplay-windows.exe;uxplay.exe|Programas (*.exe)|*.exe",
        };
        if (dialog.ShowDialog() != true) return;

        _settings.UxPlayPath = dialog.FileName;
        _settings.Save();
        Raise(nameof(UxPlayLabel));
        IsSheetOpen = false;
        StartMirror(dialog.FileName, showInstructions: true);
    }

    private void ShowSheet(string title, string body, bool showLocate)
    {
        SheetTitle = title;
        SheetBody = body;
        SheetShowLocate = showLocate;
        IsSheetOpen = true;
    }

    // ------------------------------------------------------------ modo demonstração

    private bool _demo;

    internal void SimulateState(LinkState state, string message, string deviceName, string model)
    {
        _demo = true;
        _settings.SetupCompleted = true;
        OnState(state, message);
        DeviceName = deviceName;
        ModelName = IPhoneModels.Friendly(model);
    }

    internal void SimulateBattery(int level) => OnBattery(level);
    internal void SimulateCharging(int from, int to) { OnBattery(from); OnBattery(to); }
    internal void SimulateNowPlaying(NowPlaying? np) => OnNowPlaying(np);
    internal void SimulateMediaCommands(params MediaCommand[] commands) => OnMediaCommands(commands);
    internal void SimulateNotification(AncsNotification n) => OnNotification(n);

    // ------------------------------------------------------------ utilidades

    private static void Ui(Action action) => Application.Current?.Dispatcher.InvokeAsync(action);
}
