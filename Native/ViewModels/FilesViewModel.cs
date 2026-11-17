using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using System.Net;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Native.Core;
using Native.Protocol;
using Native.Protocol.Share;
using Native.Services;

namespace Native.ViewModels;

public enum TransferDirection { ToPhone, FromPhone }

/// <summary>Uma linha da aba Arquivos: algo enviado ao iPhone ou recebido dele.</summary>
public sealed class TransferItem : ObservableObject
{
    public TransferDirection Direction { get; init; }
    public string Name { get; init; } = "";
    public long Size { get; init; }
    public string? Path { get; init; }
    public string? Text { get; init; }
    public string? ShareId { get; init; }
    public DateTime When { get; init; } = DateTime.Now;

    public bool IsText => Text is not null;
    public bool IsToPhone => Direction == TransferDirection.ToPhone;
    public bool HasPath => Path is not null;

    public string KindLabel
    {
        get
        {
            if (IsText) return Uri.TryCreate(Text!.Trim(), UriKind.Absolute, out var u) && u.Scheme.StartsWith("http") ? "LINK" : "TXT";
            var ext = System.IO.Path.GetExtension(Name).TrimStart('.').ToUpperInvariant();
            return ext.Length is > 0 and <= 4 ? ext : "ARQ";
        }
    }

    public string DirectionGlyph => IsToPhone ? "" : "";   // seta para cima / para baixo

    private string _status = "";
    public string Status { get => _status; set { if (Set(ref _status, value)) Raise(nameof(Details)); } }

    private bool _isDone;
    public bool IsDone { get => _isDone; set => Set(ref _isDone, value); }

    public string Details
    {
        get
        {
            var parts = new List<string>();
            if (!IsText) parts.Add(FilesViewModel.FormatSize(Size));
            parts.Add(TimeFormat.Relative(When));
            if (Status.Length > 0) parts.Add(Status);
            return string.Join(" · ", parts);
        }
    }

    public void Touch() => Raise(nameof(Details));

    private ImageSource? _thumbnail;
    private bool _thumbTried;
    public ImageSource? Thumbnail
    {
        get
        {
            if (_thumbTried || Path is null) return _thumbnail;
            _thumbTried = true;
            var ext = System.IO.Path.GetExtension(Path).ToLowerInvariant();
            if (ext is ".jpg" or ".jpeg" or ".png" or ".gif" or ".bmp" or ".webp" or ".heic" or ".tif" or ".tiff")
            {
                try
                {
                    var img = new BitmapImage();
                    img.BeginInit();
                    img.CacheOption = BitmapCacheOption.OnLoad;            // não prende o arquivo
                    img.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
                    img.DecodePixelWidth = 96;
                    img.UriSource = new Uri(Path);
                    img.EndInit();
                    img.Freeze();
                    _thumbnail = img;
                }
                catch
                {
                    // HEIC sem a extensão do Windows, arquivo corrompido etc.: fica o rótulo
                }
            }
            return _thumbnail;
        }
    }
    public bool HasThumbnail => Thumbnail is not null;
}

/// <summary>
/// Aba Arquivos: o "AirDrop" do Native. Liga o servidor na rede local, mostra o QR de pareamento
/// e cuida do que vai e vem entre o PC e o iPhone.
/// </summary>
public sealed class FilesViewModel : ObservableObject, IDisposable
{
    public const int DefaultPort = 47811;

    private readonly AppSettings _settings;
    private readonly Action<string, string> _banner;
    private readonly ShareServer _server;
    private MdnsResponder? _mdns;
    private readonly DispatcherTimer _presence;
    private IPAddress? _lanIp;
    private bool _wasOnline;

    public ObservableCollection<TransferItem> Items { get; } = new();

    public FilesViewModel(AppSettings settings, Action<string, string> banner)
    {
        _settings = settings;
        _banner = banner;

        if (string.IsNullOrEmpty(_settings.ShareToken))
        {
            _settings.ShareToken = ShareServer.NewToken();
            _settings.Save();
        }

        _server = new ShareServer(_settings.ShareToken!, () => ReceiveFolder)
        {
            DisplayName = Environment.MachineName,
            TouchIcon = LoadTouchIcon(),
        };
        _server.FileReceived += f => Ui(() => OnFileReceived(f));
        _server.TextReceived += t => Ui(() => OnTextReceived(t));
        _server.ItemDelivered += i => Ui(() => OnDelivered(i));
        _server.OutboxChanged += () => Ui(SyncOutbox);
        _server.PhoneSeen += () => Ui(OnPhoneSeen);
        _server.ClipboardReceived += c => Ui(() => OnClipboardReceived(c));
        ApplyClipboardSharing();

        Items.CollectionChanged += (_, _) => Raise(nameof(HasItems));

        _presence = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _presence.Tick += (_, _) =>
        {
            RaisePresence();
            foreach (var i in Items) i.Touch();
        };

        PickFilesCommand = new RelayCommand(PickFiles);
        PasteCommand = new RelayCommand(Paste);
        OpenCommand = new RelayCommand(p => { if (p is TransferItem t) Open(t); });
        ShowInFolderCommand = new RelayCommand(p => { if (p is TransferItem t && t.Path is not null) ShowInFolder(t.Path); });
        CopyTextCommand = new RelayCommand(p => { if (p is TransferItem { Text: { } text }) ClipboardHelper.TrySetText(text); });
        RemoveCommand = new RelayCommand(p => { if (p is TransferItem t) Remove(t); });
        ClearCommand = new RelayCommand(ClearDone);
        OpenFolderCommand = new RelayCommand(() => { Directory.CreateDirectory(ReceiveFolder); ShowInFolder(ReceiveFolder, select: false); });
        TogglePairingCommand = new RelayCommand(() => ShowPairingRequested = !ShowPairingRequested);
        NewKeyCommand = new RelayCommand(NewKey);
    }

    // ------------------------------------------------------------ servidor

    public void Start()
    {
        _lanIp = Network.PrimaryLanAddress();
        try
        {
            _server.Start(_settings.SharePort ?? DefaultPort);
            if (_settings.SharePort != _server.Port && !_settings.ReadOnly)
            {
                _settings.SharePort = _server.Port;
                _settings.Save();
            }
            ServerError = null;
            Log.Info("Arquivos", $"compartilhamento na porta {_server.Port} ({_lanIp?.ToString() ?? "sem rede"})");
        }
        catch (Exception ex)
        {
            ServerError = "Não consegui abrir o compartilhamento: " + ex.Message;
            Log.Error("Arquivos", "falha ao abrir o servidor", ex);
        }

        try
        {
            _mdns = new MdnsResponder(HostName, () => _lanIp ?? Network.PrimaryLanAddress());
            _mdns.Start();
        }
        catch (Exception ex)
        {
            _mdns = null;
            Log.Warn("Arquivos", "mDNS indisponível (" + ex.Message + "); o Atalho vai usar o IP");
        }

        _server.ShortcutBase = ShortcutBase;
        _presence.Start();
        RaiseAddresses();
    }

    /// <summary>Wi-Fi trocou, PC voltou da suspensão: refaz o endereço e o QR.</summary>
    public void RefreshNetwork()
    {
        var ip = Network.PrimaryLanAddress();
        if (Equals(ip, _lanIp)) return;
        _lanIp = ip;
        _server.ShortcutBase = ShortcutBase;
        RaiseAddresses();
    }

    private void RaiseAddresses()
    {
        Raise(nameof(PairUrl));
        Raise(nameof(QrGeometry));
        Raise(nameof(HasNetwork));
        Raise(nameof(AddressText));
        Raise(nameof(StatusText));
        Raise(nameof(IsRunning));
    }

    public bool IsRunning => _server.IsRunning;
    public bool HasNetwork => _lanIp is not null;

    private string? _serverError;
    public string? ServerError
    {
        get => _serverError;
        private set { if (Set(ref _serverError, value)) { Raise(nameof(HasServerError)); Raise(nameof(StatusText)); } }
    }
    public bool HasServerError => ServerError is not null;

    public string HostName => MdnsPacket.HostNameFor(Environment.MachineName);
    public int Port => _server.IsRunning ? _server.Port : _settings.SharePort ?? DefaultPort;

    /// <summary>Endereço do QR code: IP (funciona em qualquer roteador) + chave.</summary>
    public string PairUrl => _lanIp is null ? "" : $"http://{_lanIp}:{Port}/?k={Uri.EscapeDataString(_server.Token)}";

    /// <summary>O Atalho usa o nome .local (não quebra se o IP mudar); sem mDNS, cai para o IP.</summary>
    private string? ShortcutBase => _mdns?.IsRunning == true ? $"http://{HostName}:{Port}" : _lanIp is null ? null : $"http://{_lanIp}:{Port}";

    public string AddressText => _lanIp is null ? "Sem rede" : $"{_lanIp}:{Port}";

    public Geometry? QrGeometry => PairUrl.Length == 0 ? null : BuildQr(PairUrl);

    private static Geometry BuildQr(string text)
    {
        var qr = QrCode.Encode(text);
        var geo = new StreamGeometry { FillRule = FillRule.Nonzero };
        using (var g = geo.Open())
        {
            for (int y = 0; y < qr.Size; y++)
                for (int x = 0; x < qr.Size; x++)
                {
                    if (!qr[y, x]) continue;
                    int run = 1;
                    while (x + run < qr.Size && qr[y, x + run]) run++;
                    g.BeginFigure(new Point(x, y), true, true);
                    g.LineTo(new Point(x + run, y), false, false);
                    g.LineTo(new Point(x + run, y + 1), false, false);
                    g.LineTo(new Point(x, y + 1), false, false);
                    x += run - 1;
                }
            // moldura clara invisível só para o Viewbox manter a margem do QR
            g.BeginFigure(new Point(-2, -2), false, false);
            g.LineTo(new Point(qr.Size + 2, qr.Size + 2), false, false);
        }
        geo.Freeze();
        return geo;
    }

    // ------------------------------------------------------------ presença do iPhone

    public bool IsPhoneOnline => DateTime.Now - _server.LastPhoneSeen < TimeSpan.FromSeconds(7);

    public bool IsPaired => _settings.SharePaired;

    private bool _showPairingRequested;
    public bool ShowPairingRequested
    {
        get => _showPairingRequested;
        set { if (Set(ref _showPairingRequested, value)) Raise(nameof(ShowPairing)); }
    }

    public bool ShowPairing => !IsPaired || ShowPairingRequested;

    public string StatusText =>
        ServerError ?? (!HasNetwork ? "O PC não está em nenhuma rede Wi-Fi ou cabo."
        : IsPhoneOnline ? "iPhone com o Native aberto"
        : IsPaired ? "Abra o Native na Tela de Início do iPhone para receber"
        : "Escaneie o QR code com a Câmera do iPhone");

    private void OnPhoneSeen()
    {
        if (!_settings.SharePaired)
        {
            _settings.SharePaired = true;
            _settings.Save();
            Raise(nameof(IsPaired));
            Raise(nameof(ShowPairing));
            ShowPairingRequested = false;
        }
        if (!_wasOnline) RaisePresence();
    }

    private void RaisePresence()
    {
        bool online = IsPhoneOnline;
        if (online == _wasOnline) return;
        _wasOnline = online;
        Raise(nameof(IsPhoneOnline));
        Raise(nameof(StatusText));
    }

    private void NewKey()
    {
        _server.Token = ShareServer.NewToken();
        _settings.ShareToken = _server.Token;
        _settings.SharePaired = false;
        _settings.Save();
        ShowPairingRequested = true;
        Raise(nameof(IsPaired));
        Raise(nameof(ShowPairing));
        RaiseAddresses();
    }

    // ------------------------------------------------------------ PC → iPhone

    public bool HasItems => Items.Count > 0;

    /// <summary>Arquivos arrastados, colados, do "Enviar para"... Pastas viram .zip.</summary>
    public void Send(IEnumerable<string> paths)
    {
        int count = 0;
        foreach (var raw in paths)
        {
            try
            {
                var path = raw;
                if (Directory.Exists(path)) path = ZipFolder(path);
                if (!File.Exists(path)) continue;
                var share = _server.OfferFile(path);
                Items.Insert(0, new TransferItem
                {
                    Direction = TransferDirection.ToPhone, Name = share.Name, Size = share.Size, Path = path, ShareId = share.Id,
                    Status = "esperando o iPhone",
                });
                count++;
            }
            catch (Exception ex)
            {
                Log.Warn("Arquivos", $"não deu para oferecer {System.IO.Path.GetFileName(raw)}: {ex.Message}");
            }
        }
        if (count > 0) Log.Info("Arquivos", $"{count} arquivo(s) disponíveis para o iPhone");
    }

    public void SendText(string text)
    {
        text = text.Trim();
        if (text.Length == 0) return;
        var share = _server.OfferText(text);
        Items.Insert(0, new TransferItem
        {
            Direction = TransferDirection.ToPhone, Name = share.Name, Text = text, Size = share.Size, ShareId = share.Id,
            Status = "esperando o iPhone",
        });
    }

    private static string StagingFolder
    {
        get
        {
            var dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "Native");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    private static string ZipFolder(string folder)
    {
        var name = new DirectoryInfo(folder).Name;
        var zip = FileNames.Unique(StagingFolder, name + ".zip");
        ZipFile.CreateFromDirectory(folder, zip, CompressionLevel.Fastest, includeBaseDirectory: true);
        return zip;
    }

    private void PickFiles()
    {
        var dlg = new Microsoft.Win32.OpenFileDialog { Multiselect = true, Title = "Enviar para o iPhone" };
        if (dlg.ShowDialog() == true) Send(dlg.FileNames);
    }

    /// <summary>Ctrl+V: arquivos copiados no Explorer, imagem (print) ou texto.</summary>
    public void Paste()
    {
        try
        {
            if (Clipboard.ContainsFileDropList())
            {
                Send(Clipboard.GetFileDropList().Cast<string>());
            }
            else if (Clipboard.ContainsImage() && Clipboard.GetImage() is { } image)
            {
                var path = FileNames.Unique(StagingFolder, $"Imagem {DateTime.Now.ToString("yyyy-MM-dd HH.mm.ss", CultureInfo.InvariantCulture)}.png");
                using (var fs = File.Create(path))
                {
                    var enc = new PngBitmapEncoder();
                    enc.Frames.Add(BitmapFrame.Create(image));
                    enc.Save(fs);
                }
                Send(new[] { path });
            }
            else if (Clipboard.ContainsText())
            {
                SendText(Clipboard.GetText());
            }
        }
        catch (COMException)
        {
            // área de transferência ocupada por outro programa
        }
    }

    private void SyncOutbox()
    {
        var alive = _server.Outbox.Select(o => o.Id).ToHashSet();
        foreach (var item in Items.Where(i => i.IsToPhone && i.ShareId is not null && !i.IsDone && !alive.Contains(i.ShareId!)).ToList())
        {
            item.Status = "removido no iPhone";
            item.IsDone = true;
        }
    }

    private void OnDelivered(ShareItem share)
    {
        var item = Items.FirstOrDefault(i => i.ShareId == share.Id);
        if (item is null) return;
        item.Status = item.IsText ? "copiado no iPhone" : "baixado no iPhone";
        item.IsDone = true;
    }

    // ------------------------------------------------------------ iPhone → PC

    public string ReceiveFolder => string.IsNullOrWhiteSpace(_settings.ShareFolder)
        ? System.IO.Path.Combine(KnownFolders.Downloads, "Native")
        : _settings.ShareFolder!;

    public string ReceiveFolderLabel => ReceiveFolder.Replace(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "~");

    private void OnFileReceived(ReceivedFile f)
    {
        var item = new TransferItem
        {
            Direction = TransferDirection.FromPhone, Name = f.Name, Size = f.Size, Path = f.Path, Status = "recebido", IsDone = true,
        };
        Items.Insert(0, item);
        Log.Info("Arquivos", $"recebido do iPhone ({FormatSize(f.Size)})");
        FileArrived?.Invoke(item);
        _banner("Recebido do iPhone", $"{f.Name} · {FormatSize(f.Size)}");
    }

    private void OnTextReceived(string text)
    {
        bool copied = ClipboardHelper.TrySetText(text);
        Items.Insert(0, new TransferItem
        {
            Direction = TransferDirection.FromPhone, Name = text.Length > 60 ? text[..60] + "…" : text, Text = text,
            Status = copied ? "copiado" : "recebido", IsDone = true,
        });
        _banner(copied ? "Copiado do iPhone" : "Texto do iPhone", text.Length > 140 ? text[..140] + "…" : text);
    }

    // ------------------------------------------------------------ área de transferência compartilhada

    /// <summary>Deixa o iPhone ler o que está copiado no PC (Atalho "Pegar do PC" e a página).</summary>
    public bool ClipboardSharing
    {
        get => _settings.ShareClipboard;
        set
        {
            if (_settings.ShareClipboard == value) return;
            _settings.ShareClipboard = value;
            _settings.Save();
            ApplyClipboardSharing();
            Raise();
        }
    }

    private void ApplyClipboardSharing() =>
        _server.ClipboardProvider = _settings.ShareClipboard ? ReadPcClipboardAsync : null;

    private static Task<ClipData?> ReadPcClipboardAsync()
    {
        var d = Application.Current?.Dispatcher;
        if (d is null) return Task.FromResult<ClipData?>(null);
        return d.InvokeAsync(() =>
        {
            for (int attempt = 0; attempt < 5; attempt++)
            {
                try
                {
                    if (Clipboard.ContainsText()) return new ClipData(Clipboard.GetText(), null);
                    if (Clipboard.ContainsImage() && Clipboard.GetImage() is { } image)
                    {
                        using var ms = new MemoryStream();
                        var enc = new PngBitmapEncoder();
                        enc.Frames.Add(BitmapFrame.Create(image));
                        enc.Save(ms);
                        return new ClipData(null, ms.ToArray(), "image/png");
                    }
                    if (Clipboard.ContainsFileDropList())
                    {
                        // Arquivos copiados no Explorer: manda os nomes (o arquivo em si vai pela aba Arquivos).
                        var names = Clipboard.GetFileDropList().Cast<string>().Select(System.IO.Path.GetFileName);
                        return new ClipData(string.Join("\n", names), null);
                    }
                    return (ClipData?)null;
                }
                catch (COMException)
                {
                    System.Threading.Thread.Sleep(40);   // outro programa está usando a área de transferência
                }
            }
            return null;
        }).Task;
    }

    private void OnClipboardReceived(ClipData clip)
    {
        if (clip.Image is { Length: > 0 } bytes)
        {
            try
            {
                var img = new BitmapImage();
                using (var ms = new MemoryStream(bytes))
                {
                    img.BeginInit();
                    img.CacheOption = BitmapCacheOption.OnLoad;
                    img.StreamSource = ms;
                    img.EndInit();
                }
                img.Freeze();
                Clipboard.SetImage(img);

                // guarda uma cópia para aparecer em Recentes (e poder abrir depois)
                var ext = clip.ImageType switch { "image/jpeg" => ".jpg", "image/heic" => ".heic", "image/gif" => ".gif", _ => ".png" };
                var path = FileNames.Unique(StagingFolder, $"Copiado do iPhone {DateTime.Now.ToString("yyyy-MM-dd HH.mm.ss", CultureInfo.InvariantCulture)}{ext}");
                File.WriteAllBytes(path, bytes);
                Items.Insert(0, new TransferItem
                {
                    Direction = TransferDirection.FromPhone, Name = "Imagem copiada no iPhone", Size = bytes.Length, Path = path,
                    Status = "na área de transferência", IsDone = true,
                });
                _banner("Copiado do iPhone", "Imagem pronta para colar (Ctrl + V).");
            }
            catch (Exception ex)
            {
                // HEIC sem a extensão do Windows não decodifica: pelo menos salva o arquivo
                Log.Warn("Arquivos", "imagem do iPhone não entrou na área de transferência: " + ex.Message);
                _banner("Imagem do iPhone", "Não deu para colar este formato. Envie pelo \"Enviar para o PC\".");
            }
            return;
        }

        var text = clip.Text ?? "";
        if (clip.IsRtf) text = RtfToText(text);
        if (text.Trim().Length > 0) OnTextReceived(text.Trim());
    }

    private static string RtfToText(string rtf)
    {
        try
        {
            var doc = new System.Windows.Documents.FlowDocument();
            var range = new System.Windows.Documents.TextRange(doc.ContentStart, doc.ContentEnd);
            using var ms = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(rtf));
            range.Load(ms, DataFormats.Rtf);
            return range.Text;
        }
        catch
        {
            return rtf;
        }
    }

    /// <summary>Chegou arquivo (a janela pode querer abrir a aba Arquivos).</summary>
    public event Action<TransferItem>? FileArrived;

    // ------------------------------------------------------------ ações

    private static void Open(TransferItem t)
    {
        if (t.Text is { } text)
        {
            if (Uri.TryCreate(text.Trim(), UriKind.Absolute, out var u) && u.Scheme.StartsWith("http", StringComparison.Ordinal))
                Shell(u.ToString());
            else ClipboardHelper.TrySetText(text);
            return;
        }
        if (t.Path is not null && File.Exists(t.Path)) Shell(t.Path);
    }

    private static void ShowInFolder(string path, bool select = true)
    {
        try
        {
            if (select && File.Exists(path)) Process.Start("explorer.exe", $"/select,\"{path}\"");
            else Process.Start("explorer.exe", $"\"{path}\"");
        }
        catch (Exception ex) { Log.Warn("Arquivos", "não abriu o Explorer: " + ex.Message); }
    }

    private static void Shell(string target)
    {
        try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); }
        catch (Exception ex) { Log.Warn("Arquivos", "não abriu: " + ex.Message); }
    }

    private void Remove(TransferItem t)
    {
        if (t.ShareId is not null) _server.Remove(t.ShareId);
        Items.Remove(t);
    }

    private void ClearDone()
    {
        foreach (var t in Items.Where(i => i.IsDone).ToList()) Items.Remove(t);
    }

    public ICommand PickFilesCommand { get; }
    public ICommand PasteCommand { get; }
    public ICommand OpenCommand { get; }
    public ICommand ShowInFolderCommand { get; }
    public ICommand CopyTextCommand { get; }
    public ICommand RemoveCommand { get; }
    public ICommand ClearCommand { get; }
    public ICommand OpenFolderCommand { get; }
    public ICommand TogglePairingCommand { get; }
    public ICommand NewKeyCommand { get; }

    // ------------------------------------------------------------ util

    public static string FormatSize(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:0} KB",
        < 1024L * 1024 * 1024 => $"{bytes / 1048576.0:0.#} MB",
        _ => $"{bytes / 1073741824.0:0.##} GB",
    };

    private static byte[]? LoadTouchIcon()
    {
        try
        {
            using var s = typeof(FilesViewModel).Assembly.GetManifestResourceStream("Native.Assets.icone-256.png");
            if (s is null) return null;
            using var ms = new MemoryStream();
            s.CopyTo(ms);
            return ms.ToArray();
        }
        catch { return null; }
    }

    private static void Ui(Action action)
    {
        var d = Application.Current?.Dispatcher;
        if (d is null || d.CheckAccess()) action();
        else d.InvokeAsync(action);
    }

    // demonstração
    internal void SimulateHistory()
    {
        _lanIp ??= IPAddress.Parse("192.168.0.23");   // a máquina de teste não tem rede
        RaiseAddresses();
        Items.Add(new TransferItem { Direction = TransferDirection.ToPhone, Name = "Proposta comercial.pdf", Size = 2_480_000, Status = "baixado no iPhone", IsDone = true, When = DateTime.Now.AddMinutes(-3) });
        Items.Add(new TransferItem { Direction = TransferDirection.FromPhone, Name = "IMG_4821.jpg", Size = 3_912_000, Status = "recebido", IsDone = true, When = DateTime.Now.AddMinutes(-8) });
        Items.Add(new TransferItem { Direction = TransferDirection.FromPhone, Name = "https://www.apple.com/br/iphone/", Text = "https://www.apple.com/br/iphone/", Status = "copiado", IsDone = true, When = DateTime.Now.AddMinutes(-21) });
        Items.Insert(0, new TransferItem { Direction = TransferDirection.ToPhone, Name = "Planilha de gastos.xlsx", Size = 48_200, Status = "esperando o iPhone" });
    }

    public void Dispose()
    {
        _presence.Stop();
        _mdns?.Dispose();
        _server.Dispose();
    }
}

/// <summary>Pastas conhecidas do Windows (a de Downloads pode ter sido movida pelo usuário).</summary>
internal static class KnownFolders
{
    private static readonly Guid DownloadsId = new("374DE290-123F-4565-9164-39C4925E467B");

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHGetKnownFolderPath([MarshalAs(UnmanagedType.LPStruct)] Guid rfid, uint flags, IntPtr token, out IntPtr path);

    public static string Downloads
    {
        get
        {
            try
            {
                if (SHGetKnownFolderPath(DownloadsId, 0, IntPtr.Zero, out var p) == 0)
                {
                    var path = Marshal.PtrToStringUni(p);
                    Marshal.FreeCoTaskMem(p);
                    if (!string.IsNullOrEmpty(path)) return path;
                }
            }
            catch { }
            return System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        }
    }
}
