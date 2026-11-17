using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Native.Models;

namespace Native.Services;

/// <summary>
/// Resolve nome e ícone de cada app do iPhone a partir do bundle id
/// (catálogo da App Store), com cache local para funcionar offline.
/// Deve ser usado na thread da interface.
/// </summary>
public sealed class AppCatalog
{
    private static readonly Dictionary<string, (string Name, string Color)> SystemApps = new(StringComparer.OrdinalIgnoreCase)
    {
        ["com.apple.MobileSMS"]        = ("Mensagens", "#30D158"),
        ["com.apple.mobilephone"]      = ("Telefone", "#30D158"),
        ["com.apple.facetime"]         = ("FaceTime", "#30D158"),
        ["com.apple.mobilemail"]       = ("Mail", "#0A84FF"),
        ["com.apple.mobilecal"]        = ("Calendário", "#FF453A"),
        ["com.apple.reminders"]        = ("Lembretes", "#FF9F0A"),
        ["com.apple.mobiletimer"]      = ("Relógio", "#3A3A3C"),
        ["com.apple.Health"]           = ("Saúde", "#FF375F"),
        ["com.apple.findmy"]           = ("Buscar", "#30D158"),
        ["com.apple.AppStore"]         = ("App Store", "#0A84FF"),
        ["com.apple.mobileslideshow"]  = ("Fotos", "#FF9F0A"),
        ["com.apple.Passbook"]         = ("Carteira", "#3A3A3C"),
        ["com.apple.shortcuts"]        = ("Atalhos", "#5E5CE6"),
        ["com.apple.Preferences"]      = ("Ajustes", "#8E8E93"),
        ["com.apple.weather"]          = ("Tempo", "#0A84FF"),
        ["com.apple.Music"]            = ("Música", "#FF375F"),
        ["com.apple.Home"]             = ("Casa", "#FF9F0A"),
        ["com.apple.mobilenotes"]      = ("Notas", "#FFD60A"),
        ["com.apple.InCallService"]    = ("Telefone", "#30D158"),
    };

    private static readonly Dictionary<string, string> PreferredNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["net.whatsapp.WhatsApp"]    = "WhatsApp",
        ["net.whatsapp.WhatsAppSMB"] = "WhatsApp Business",
        ["com.burbn.instagram"]      = "Instagram",
        ["ph.telegra.Telegraph"]     = "Telegram",
        ["com.facebook.Facebook"]    = "Facebook",
        ["com.facebook.Messenger"]   = "Messenger",
        ["com.google.Gmail"]         = "Gmail",
        ["com.linkedin.LinkedIn"]    = "LinkedIn",
        ["com.microsoft.Office.Outlook"] = "Outlook",
        ["com.microsoft.skype.teams"] = "Teams",
        ["com.spotify.client"]       = "Spotify",
    };

    private sealed class CachedApp
    {
        public string Name { get; set; } = "";
        public string? IconFile { get; set; }
    }

    private readonly AppSettings _settings;
    private readonly Dictionary<string, AppInfo> _apps = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, CachedApp> _cache;
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(8) };
    private readonly string _iconDir;
    private readonly string _indexPath;

    public AppCatalog(AppSettings settings)
    {
        _settings = settings;
        _iconDir = Path.Combine(AppSettings.Folder, "icons");
        _indexPath = Path.Combine(AppSettings.Folder, "apps.json");
        Directory.CreateDirectory(_iconDir);
        _cache = LoadCache();
    }

    public ObservableCollection<AppInfo> Apps { get; } = new();

    public event Action<AppInfo>? PermissionChanged;

    /// <summary>Pergunta ao iPhone o nome do app como aparece na tela de início (definido pelo app).</summary>
    public Func<string, Task<string?>>? AppNameResolver { get; set; }

    public AppInfo Get(string appId)
    {
        if (string.IsNullOrWhiteSpace(appId)) appId = "desconhecido";
        if (_apps.TryGetValue(appId, out var existing)) return existing;

        var app = new AppInfo(appId, FallbackName(appId))
        {
            IsAllowed = !_settings.MutedApps.Contains(appId)
        };

        if (SystemApps.TryGetValue(appId, out var sys))
        {
            app.DisplayName = sys.Name;
            app.FallbackBrush = AppInfo.BrushFor(appId, sys.Color);
        }
        else if (_cache.TryGetValue(appId, out var cached))
        {
            app.DisplayName = PreferredNames.TryGetValue(appId, out var pn) ? pn : cached.Name;
            if (cached.IconFile is not null) app.Icon = LoadIcon(Path.Combine(_iconDir, cached.IconFile));
        }
        else
        {
            if (PreferredNames.TryGetValue(appId, out var pn)) app.DisplayName = pn;
            _ = LookupAsync(app);
        }

        app.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(AppInfo.IsAllowed)) return;
            if (app.IsAllowed) _settings.MutedApps.Remove(app.AppId);
            else _settings.MutedApps.Add(app.AppId);
            _settings.Save();
            PermissionChanged?.Invoke(app);
        };

        _apps[appId] = app;
        InsertSorted(app);
        return app;
    }

    private void InsertSorted(AppInfo app)
    {
        int i = 0;
        while (i < Apps.Count && string.Compare(Apps[i].DisplayName, app.DisplayName, StringComparison.CurrentCultureIgnoreCase) < 0) i++;
        Apps.Insert(i, app);
    }

    private async Task LookupAsync(AppInfo app)
    {
        if (app.AppId == "desconhecido") return;

        // O nome que a pessoa vê no iPhone ("WhatsApp", não "WhatsApp Messenger") vem do próprio iPhone.
        string? phoneName = null;
        if (AppNameResolver is not null && !PreferredNames.ContainsKey(app.AppId))
        {
            try { phoneName = await AppNameResolver(app.AppId); } catch { }
            if (phoneName is not null) app.DisplayName = phoneName;
        }

        try
        {
            var url = $"https://itunes.apple.com/lookup?bundleId={Uri.EscapeDataString(app.AppId)}&country=br";
            var json = await _http.GetStringAsync(url);
            using var doc = JsonDocument.Parse(json);
            var results = doc.RootElement.GetProperty("results");

            var entry = new CachedApp { Name = phoneName ?? app.DisplayName };
            if (results.GetArrayLength() > 0)
            {
                var r = results[0];
                if (phoneName is null && !PreferredNames.ContainsKey(app.AppId) &&
                    r.TryGetProperty("trackName", out var tn) && tn.GetString() is { Length: > 0 } name)
                {
                    entry.Name = CleanName(name);
                    app.DisplayName = entry.Name;
                }

                string? art = r.TryGetProperty("artworkUrl100", out var au) ? au.GetString() : null;
                if (!string.IsNullOrEmpty(art))
                {
                    art = art.Replace("100x100bb", "128x128bb");
                    var bytes = await _http.GetByteArrayAsync(art);
                    var file = SafeFileName(app.AppId) + Path.GetExtension(new Uri(art).AbsolutePath);
                    await File.WriteAllBytesAsync(Path.Combine(_iconDir, file), bytes);
                    entry.IconFile = file;
                    app.Icon = LoadIcon(Path.Combine(_iconDir, file));
                }
            }

            _cache[app.AppId] = entry;
            SaveCache();
        }
        catch
        {
            // Sem internet ou app fora da App Store: fica com o ícone de letra.
        }
    }

    private static ImageSource? LoadIcon(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.DecodePixelWidth = 96;
            bmp.UriSource = new Uri(path);
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch
        {
            return null;
        }
    }

    private static string FallbackName(string appId)
    {
        var last = appId.Split('.')[^1];
        if (last.Length == 0) return appId;
        return char.ToUpperInvariant(last[0]) + last[1..];
    }

    private static string CleanName(string name)
    {
        foreach (var sep in new[] { " - ", " – ", " — ", ": ", " | " })
        {
            int i = name.IndexOf(sep, StringComparison.Ordinal);
            if (i > 0) name = name[..i];
        }
        return name.Trim();
    }

    private static string SafeFileName(string s)
    {
        foreach (var c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
        return s;
    }

    private Dictionary<string, CachedApp> LoadCache()
    {
        try
        {
            if (File.Exists(_indexPath))
            {
                var data = JsonSerializer.Deserialize<Dictionary<string, CachedApp>>(File.ReadAllText(_indexPath));
                if (data is not null) return new Dictionary<string, CachedApp>(data, StringComparer.OrdinalIgnoreCase);
            }
        }
        catch { }
        return new Dictionary<string, CachedApp>(StringComparer.OrdinalIgnoreCase);
    }

    private void SaveCache()
    {
        try { File.WriteAllText(_indexPath, JsonSerializer.Serialize(_cache)); }
        catch { }
    }
}
