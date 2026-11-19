using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Native.Bluetooth;
using Native.Core;
using Native.Models;
using Native.Services;
using Native.ViewModels;
using Microsoft.Win32;
using Forms = System.Windows.Forms;

namespace Native;

public partial class App : Application
{
    private const string InstanceName = "Native.SingleInstance";
    private const string ShowSignalName = "Native.ShowWindow";
    private const string SendPipeName = "Native.Enviar";

    private Mutex? _singleInstance;
    private EventWaitHandle? _showSignal;
    private readonly CancellationTokenSource _cts = new();
    private Forms.NotifyIcon? _tray;
    private Forms.ToolStripMenuItem? _dndMenuItem;
    private Forms.ToolStripMenuItem? _mirrorMenuItem;
    private Forms.ToolStripMenuItem? _statusMenuItem;
    private AncsClient? _client;
    private MessageService? _messages;
    private ContactService? _contacts;
    private MainViewModel? _vm;
    private MainWindow? _main;
    private BannerWindow? _banner;
    private PipWindow? _pip;
    private MiniWindow? _mini;
    private Forms.ToolStripMenuItem? _pipMenuItem;
    private GlobalHotKey? _hotKey;
    private bool _exiting;
    private bool _popoverMode;
    private DateTime _lastAutoHide = DateTime.MinValue;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        try
        {
            Start(e);
        }
        catch (Exception ex)
        {
            Log.Error("App", "Falha ao iniciar", ex);
            MessageBox.Show($"O Native não conseguiu iniciar.\n\n{Log.FirstLine(ex.Message)}\n\nDetalhes em {Log.Folder}",
                "Native", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    private void Start(StartupEventArgs e)
    {
        // "Reabrir agora": espera a instância anterior fechar (Bluetooth e porta liberados) antes de seguir.
        int wi = Array.IndexOf(e.Args, "--wait-pid");
        if (wi >= 0 && wi + 1 < e.Args.Length && int.TryParse(e.Args[wi + 1], out int oldPid))
        {
            try { System.Diagnostics.Process.GetProcessById(oldPid).WaitForExit(15000); } catch { }
        }

        _singleInstance = new Mutex(true, InstanceName, out bool isFirst);
        var toSend = FilesToSend(e.Args);
        if (!isFirst)
        {
            // Já está aberto: entrega os arquivos do "Enviar para" ou só pede para a janela aparecer.
            if (toSend.Length > 0 && TrySendToRunningInstance(toSend)) { Shutdown(); return; }
            try { EventWaitHandle.OpenExisting(ShowSignalName).Set(); } catch { }
            Shutdown();
            return;
        }

        AppSettings.MigrateFromOldName();
        InstallCrashHandlers();
        Log.Prune();
        Log.Info("App", $"Iniciando {typeof(App).Assembly.GetName().Version?.ToString(3)} no Windows {Environment.OSVersion.Version}");

        var settings = AppSettings.Load();
        WindowEffects.BlurEngine = Math.Clamp(settings.GlassEngine, 1, 3);
        ThemeManager.Initialize(settings.Theme, settings.Glass, settings.GlassOpacity, settings.GlassBlur);

        var catalog = new AppCatalog(settings);
        _client = new AncsClient();
        Func<ulong?> address = () => _client.DeviceAddress;
        _messages = new MessageService(address);
        _contacts = new ContactService(AppSettings.Folder, address);
        var calls = new CallService(address);

        _vm = new MainViewModel(_client, catalog, settings, _messages, _contacts, calls);
        _vm.BannerRequested += OnBannerRequested;
        _vm.NotificationGone += uid => _banner?.DismissIfShowing(uid);
        catalog.AppNameResolver = id => _client.GetAppDisplayNameAsync(id);

        _main = new MainWindow { DataContext = _vm };
        _vm.RestartRequested += Restart;
        _main.Closing += (_, args) =>
        {
            if (_exiting) return;
            args.Cancel = true;      // fechar = ir para a bandeja, como no macOS
            _main.Hide();
        };
        // Aberta pela bandeja ou pelo atalho, a janela se comporta como a Central do macOS:
        // some ao clicar fora (a menos que esteja fixada ou com um painel aberto).
        _main.Deactivated += (_, _) =>
        {
            if (!_popoverMode || _main.Topmost || _vm.IsSheetOpen || !_main.IsVisible) return;
            if (_main.OwnedWindows.Count > 0) return;
            _lastAutoHide = DateTime.Now;
            _main.Hide();
        };

        _banner = new BannerWindow(_vm);
        _banner.OpenRequested += () => ShowMain(popover: true);

        _vm.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(MainViewModel.IsPipOpen)) SyncPip();
            if (args.PropertyName == nameof(MainViewModel.IsMiniMode)) SyncMini();
        };

        CreateTray();
        WatchSystem();
        ListenForSecondInstance();

        // Ctrl + Alt + N abre e fecha o Native
        _hotKey = new GlobalHotKey(_main, GlobalHotKey.ModControl | GlobalHotKey.ModAlt, 0x4E, ToggleMain);
        if (!_hotKey.IsRegistered) Log.Warn("App", "Ctrl+Alt+N já está em uso por outro programa");

        _vm.Files.Start();
        System.Net.NetworkInformation.NetworkChange.NetworkAddressChanged += (_, _) =>
            Dispatcher.InvokeAsync(() => _vm?.Files.RefreshNetwork());
        ListenForFilesToSend();

        if (_vm.StartInMiniMode && !e.Args.Contains("--full")) _vm.IsMiniMode = true;   // abre como estava
        else if (e.Args.Contains("--mini")) _vm.IsMiniMode = true;
        else if (!e.Args.Contains("--minimized")) _main.Show();
        if (toSend.Length > 0) Dispatcher.InvokeAsync(() => ReceiveSendRequest(toSend), DispatcherPriority.ApplicationIdle);

        if (e.Args.Contains("--demo"))
        {
            // Interface com dados de exemplo, sem Bluetooth (para capturas de tela e testes visuais).
            settings.ReadOnly = true;
            DemoData.Apply(_vm);
            _vm.Files.SimulateHistory();
            if (e.Args.Contains("--noplayer")) _vm.SimulateNowPlaying(null);
            if (e.Args.FirstOrDefault(a => a.StartsWith("--tab=", StringComparison.Ordinal)) is { } tab &&
                Enum.TryParse<AppTab>(tab[6..], true, out var selected))
                _vm.SelectedTab = selected;
            if (e.Args.Contains("--light")) ThemeManager.Apply("claro");
            if (e.Args.Contains("--pip")) _vm.IsPipOpen = true;
            if (e.Args.FirstOrDefault(a => a.StartsWith("--banner=", StringComparison.Ordinal)) is { } banner)
            {
                var vm = _vm;
                var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
                timer.Tick += (_, _) => { timer.Stop(); DemoData.Incoming(vm, banner[9..]); };
                timer.Start();
            }
            return;
        }

        if (settings.AirPlayAlwaysOn) _vm.StartAirPlayInBackground();

        _ = _client.RunAsync(_cts.Token);
    }

    // ------------------------------------------------------------ bandeja

    private void CreateTray()
    {
        var menu = new Forms.ContextMenuStrip();
        DarkMenuColors.Apply(menu);

        _statusMenuItem = new Forms.ToolStripMenuItem("Procurando iPhone…") { Enabled = false };
        menu.Items.Add(_statusMenuItem);
        menu.Items.Add(new Forms.ToolStripSeparator());

        var open = new Forms.ToolStripMenuItem("Abrir Native", null, (_, _) => ShowMain(popover: true))
        {
            ShortcutKeyDisplayString = "Ctrl+Alt+N",
        };
        open.Font = new System.Drawing.Font(menu.Font, System.Drawing.FontStyle.Bold);
        menu.Items.Add(open);
        menu.Items.Add(new Forms.ToolStripMenuItem("Mensagens", null, (_, _) => ShowTab(AppTab.Messages)));
        menu.Items.Add(new Forms.ToolStripMenuItem("Telefone", null, (_, _) => ShowTab(AppTab.Phone)));

        _mirrorMenuItem = new Forms.ToolStripMenuItem("Espelhar iPhone", null, (_, _) =>
        {
            ShowMain(popover: false);
            _vm?.MirrorCommand.Execute(null);
        });
        menu.Items.Add(_mirrorMenuItem);

        _pipMenuItem = new Forms.ToolStripMenuItem("Mini player flutuante", null, (_, _) =>
        {
            if (_vm is not null) _vm.IsPipOpen = !_vm.IsPipOpen;
        });
        menu.Items.Add(_pipMenuItem);

        var miniItem = new Forms.ToolStripMenuItem("Mini Native (fixo na tela)", null, (_, _) =>
        {
            if (_vm is not null) _vm.IsMiniMode = !_vm.IsMiniMode;
        });
        menu.Items.Add(miniItem);
        menu.Items.Add(new Forms.ToolStripMenuItem("Enviar arquivos para o iPhone…", null, (_, _) =>
        {
            ShowTab(AppTab.Files);
            _vm?.Files.PickFilesCommand.Execute(null);
        }));

        _dndMenuItem = new Forms.ToolStripMenuItem("Não Perturbe", null, (_, _) =>
        {
            if (_vm is not null) _vm.DoNotDisturb = !_vm.DoNotDisturb;
        });
        menu.Items.Add(_dndMenuItem);

        var pinItem = new Forms.ToolStripMenuItem("Manter na frente de tudo", null, (_, _) =>
        {
            if (_vm is not null) _vm.KeepOnTop = !_vm.KeepOnTop;
        });
        menu.Items.Add(pinItem);

        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(new Forms.ToolStripMenuItem("Ajustes…", null, (_, _) => ShowTab(AppTab.Settings)));
        menu.Items.Add(new Forms.ToolStripMenuItem("Sair do Native", null, (_, _) => ExitApp()));

        menu.Opening += (_, _) =>
        {
            if (_vm is null) return;
            _dndMenuItem.Checked = _vm.DoNotDisturb;
            pinItem.Checked = _vm.KeepOnTop;
            _pipMenuItem.Checked = _vm.IsPipOpen;
            miniItem.Checked = _vm.IsMiniMode;
            _mirrorMenuItem.Text = _vm.IsMirroring ? "Parar espelhamento" : "Espelhar iPhone";
            _statusMenuItem.Text = _vm.IsConnected
                ? $"{_vm.DeviceName} · {(_vm.HasBattery ? _vm.BatteryText : "conectado")}"
                : _vm.StatusText;
        };

        _tray = new Forms.NotifyIcon
        {
            Icon = TrayIconFactory.Create(),
            Text = "Native",
            Visible = true,
            ContextMenuStrip = menu,
        };
        _tray.MouseClick += (_, args) =>
        {
            if (args.Button != Forms.MouseButtons.Left) return;
            // O clique na bandeja tira o foco da janela antes de chegar aqui; se ela acabou de sumir, o clique era para fechar.
            if ((DateTime.Now - _lastAutoHide).TotalMilliseconds < 350) return;
            ToggleMain();
        };
    }

    private void ShowTab(AppTab tab)
    {
        if (_vm is not null) _vm.SelectedTab = tab;
        ShowMain(popover: false);
    }

    private void ToggleMain()
    {
        if (_main is null) return;
        if (_vm is { IsMiniMode: true } && _mini is not null)
        {
            if (_mini.IsVisible) _mini.Hide();
            else _mini.Show();
            return;
        }
        if (_main.IsVisible && _main.IsActive)
        {
            _main.Hide();
            return;
        }
        ShowMain(popover: true);
    }

    private void ShowMain(bool popover)
    {
        if (_main is null) return;
        if (_vm is { IsMiniMode: true } && _mini is not null && popover)
        {
            _mini.Show();   // no modo mini, a bandeja e o atalho mostram o mini
            return;
        }
        _popoverMode = popover;

        if (popover && !_main.IsVisible)
        {
            // Ancora no canto inferior direito, acima da bandeja, como um menu do sistema
            var area = SystemParameters.WorkArea;
            _main.Left = area.Right - _main.Width - 12;
            _main.Top = Math.Max(area.Top + 12, area.Bottom - _main.Height - 12);
        }

        _main.Show();
        if (_main.WindowState == WindowState.Minimized) _main.WindowState = WindowState.Normal;
        _main.Activate();
    }

    private void OnBannerRequested(PhoneNotification notification)
    {
        if (_vm is null || _banner is null) return;
        if (!_vm.ShouldAlert(notification)) return;

        if (_vm.PlaySound) NotificationSound.Play();

        if (!_vm.ShowBanners && !notification.IsCall) return;
        if (_main is { IsVisible: true, IsActive: true } && !notification.IsCall) return;   // janela em foco: a lista já mostra
        _banner.Present(notification);
    }

    // ------------------------------------------------------------ sistema

    private void WatchSystem()
    {
        // Ao voltar da suspensão ou desbloquear a sessão, o Bluetooth costuma ter caído sem aviso.
        SystemEvents.PowerModeChanged += (_, e) =>
        {
            if (e.Mode == PowerModes.Resume) Dispatcher.InvokeAsync(() => _vm?.RequestReconnect("PC voltou da suspensão"));
        };
        SystemEvents.SessionSwitch += (_, e) =>
        {
            if (e.Reason == SessionSwitchReason.SessionUnlock) Dispatcher.InvokeAsync(() => _vm?.RequestReconnect("sessão desbloqueada"));
        };

        if (_vm is not null)
        {
            _vm.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName is nameof(MainViewModel.StatusText) or nameof(MainViewModel.BatteryText) && _tray is not null)
                {
                    var text = _vm.IsConnected ? $"Native · {_vm.DeviceName} {_vm.BatteryText}" : $"Native · {_vm.StatusText}";
                    _tray.Text = text.Length > 63 ? text[..63] : text;
                }
            };
        }
    }

    private void ListenForSecondInstance()
    {
        _showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, ShowSignalName);
        var thread = new Thread(() =>
        {
            while (!_cts.IsCancellationRequested)
            {
                if (_showSignal.WaitOne(1000) && !_cts.IsCancellationRequested)
                    Dispatcher.InvokeAsync(() => ShowMain(popover: false));
            }
        })
        { IsBackground = true, Name = "Native.SegundaInstancia" };
        thread.Start();
    }

    // ------------------------------------------------------------ "Enviar para > iPhone" do Explorer

    /// <summary>Caminhos depois de --send (o atalho do "Enviar para" chama Native.exe --send "arquivo"...).</summary>
    private static string[] FilesToSend(string[] args)
    {
        int i = Array.IndexOf(args, "--send");
        return i < 0 ? Array.Empty<string>() : args.Skip(i + 1).Where(a => !a.StartsWith("--", StringComparison.Ordinal)).ToArray();
    }

    private static bool TrySendToRunningInstance(string[] paths)
    {
        try
        {
            using var pipe = new System.IO.Pipes.NamedPipeClientStream(".", SendPipeName, System.IO.Pipes.PipeDirection.Out);
            pipe.Connect(2000);
            using var w = new System.IO.StreamWriter(pipe, new System.Text.UTF8Encoding(false));
            foreach (var p in paths) w.WriteLine(System.IO.Path.GetFullPath(p));
            return true;
        }
        catch (Exception ex)
        {
            Log.Warn("App", "não deu para entregar os arquivos à janela aberta: " + ex.Message);
            return false;
        }
    }

    private void ListenForFilesToSend()
    {
        var thread = new Thread(() =>
        {
            while (!_cts.IsCancellationRequested)
            {
                try
                {
                    using var pipe = new System.IO.Pipes.NamedPipeServerStream(SendPipeName, System.IO.Pipes.PipeDirection.In, 1,
                        System.IO.Pipes.PipeTransmissionMode.Byte, System.IO.Pipes.PipeOptions.Asynchronous);
                    pipe.WaitForConnectionAsync(_cts.Token).GetAwaiter().GetResult();
                    using var r = new System.IO.StreamReader(pipe, System.Text.Encoding.UTF8);
                    var paths = new System.Collections.Generic.List<string>();
                    string? line;
                    while ((line = r.ReadLine()) is not null) if (line.Length > 0) paths.Add(line);
                    if (paths.Count > 0) Dispatcher.InvokeAsync(() => ReceiveSendRequest(paths.ToArray()));
                }
                catch (OperationCanceledException) { return; }
                catch (Exception ex)
                {
                    Log.Warn("App", "canal do Enviar para: " + ex.Message);
                    Thread.Sleep(500);
                }
            }
        })
        { IsBackground = true, Name = "Native.EnviarPara" };
        thread.Start();
    }

    private void ReceiveSendRequest(string[] paths)
    {
        if (_vm is null) return;
        _vm.Files.Send(paths);
        ShowTab(AppTab.Files);
    }

    private void InstallCrashHandlers()
    {
        DispatcherUnhandledException += (_, e) =>
        {
            Log.Error("App", "Erro não tratado na interface", e.Exception);
            e.Handled = true;      // não derruba o app por um erro de tela
        };
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            Log.Error("App", "Erro fatal", e.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Log.Error("App", "Erro em tarefa de fundo", e.Exception);
            e.SetObserved();
        };
    }

    // ------------------------------------------------------------ saída

    /// <summary>Liga ou desliga o mini Native: ele substitui a janela grande enquanto estiver ligado.</summary>
    private void SyncMini()
    {
        if (_vm is null || _main is null) return;
        if (_vm.IsMiniMode)
        {
            if (_mini is null)
            {
                _mini = new MiniWindow(_vm);
                _mini.ExpandRequested += tab =>
                {
                    if (tab is { } t) _vm.SelectedTab = t;
                    _vm.IsMiniMode = false;
                };
                _mini.HideRequested += () => _mini?.Hide();
                _mini.Closed += (_, _) => _mini = null;
            }
            _main.Hide();
            _mini.Show();
        }
        else
        {
            var mini = _mini;
            _mini = null;
            mini?.Close();
            ShowMain(popover: false);
        }
    }

    /// <summary>Abre ou fecha o mini player conforme o view model.</summary>
    private void SyncPip()
    {
        if (_vm is null) return;
        if (_vm.IsPipOpen && _pip is null)
        {
            _pip = new PipWindow(_vm);
            _pip.OpenRequested += () => ShowMain(popover: false);
            _pip.Closed += (_, _) => _pip = null;
            _pip.Show();
        }
        else if (!_vm.IsPipOpen && _pip is not null)
        {
            var pip = _pip;
            _pip = null;
            pip.Close();
        }
    }

    /// <summary>Fecha e abre de novo (para aplicar o vidro na janela principal).</summary>
    private void Restart()
    {
        var exe = Environment.ProcessPath;
        if (exe is null) return;
        try
        {
            var args = $"--wait-pid {Environment.ProcessId}" + (_vm?.IsMiniMode == true ? "" : " --full");
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exe, args) { UseShellExecute = false });
        }
        catch (Exception ex)
        {
            Log.Error("App", "Não deu para reabrir", ex);
            return;
        }
        ExitApp();
    }

    private void ExitApp()
    {
        _pip?.Close();
        _mini?.Close();
        _exiting = true;
        _cts.Cancel();
        _vm?.StopMirror();
        _hotKey?.Dispose();
        if (_tray is not null)
        {
            _tray.Visible = false;
            _tray.Dispose();
        }
        _banner?.Close();
        _main?.Close();
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Log.Info("App", "Encerrando");
        _cts.Cancel();
        _vm?.StopMirror();
        _hotKey?.Dispose();
        _messages?.Dispose();
        _contacts?.Dispose();
        _vm?.Files.Dispose();
        if (_tray is not null) _tray.Visible = false;
        try { _singleInstance?.ReleaseMutex(); } catch { }
        base.OnExit(e);
    }
}
