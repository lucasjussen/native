using System;
using System.Linq;
using System.Windows;
using Native.Core;
using Microsoft.Win32;

namespace Native.Services;

/// <summary>Tema claro/escuro: segue o Windows ou fica fixo, e troca na hora sem reiniciar.</summary>
public static class ThemeManager
{
    private static readonly Uri DarkUri = new("Themes/Dark.xaml", UriKind.Relative);
    private static readonly Uri LightUri = new("Themes/Light.xaml", UriKind.Relative);
    private static readonly Uri GlassDarkUri = new("Themes/GlassDark.xaml", UriKind.Relative);
    private static readonly Uri GlassLightUri = new("Themes/GlassLight.xaml", UriKind.Relative);
    private static string _mode = "sistema";

    /// <summary>Liquid Glass: uma camada de pincéis translúcidos por cima da paleta clara/escura.</summary>
    public static bool Glass { get; private set; }
    public static double GlassOpacity { get; private set; } = 0.62;

    /// <summary>A janela principal só fica transparente se abriu com o vidro ligado (o Windows não troca isso com ela aberta).</summary>
    public static bool WindowGlassAtStartup { get; private set; }

    public static event Action? GlassChanged;

    /// <summary>Desfoque do que está atrás (vidro fosco). Desligado, o vidro fica só translúcido.</summary>
    public static bool GlassBlur { get; private set; } = true;
    public static bool BlurActive => Glass && GlassBlur && WindowEffects.BlurSupported;

    public static void SetGlassBlur(bool on)
    {
        GlassBlur = on;
        Apply(_mode);
        GlassChanged?.Invoke();
    }

    /// <summary>
    /// Mini e mini player: com desfoque, o Windows desenha borda, cantos e sombra; sem ele, a janela desenha
    /// os dela (com margem para a sombra). Os XAML pegam isso por DynamicResource.
    /// </summary>
    private static void ApplyFloatLayout()
    {
        var r = Application.Current.Resources;
        bool blur = BlurActive;
        r["FloatMargin"] = blur ? new Thickness(0) : new Thickness(14, 10, 14, 18);
        r["FloatRadius"] = blur ? new CornerRadius(8) : new CornerRadius(18);
        r["FloatShadow"] = blur ? Visibility.Collapsed : Visibility.Visible;
    }

    public static void SetGlass(bool on)
    {
        Glass = on;
        Apply(_mode);
        GlassChanged?.Invoke();
    }

    // Os fundos translúcidos são pincéis "vivos": o controle de transparência só muda a Opacity deles,
    // e o WPF redesenha na hora — sem trocar recursos nem reler o XAML a cada movimento (era isso que travava).
    private static readonly System.Collections.Generic.List<System.Windows.Media.Brush> LiveBrushes = new();

    public static void SetGlassOpacity(double opacity)
    {
        GlassOpacity = Math.Clamp(opacity, 0.25, 1);
        foreach (var b in LiveBrushes) b.Opacity = GlassOpacity;
    }

    private static void Tint(ResourceDictionary glass)
    {
        LiveBrushes.Clear();
        foreach (var key in SeeThrough)
            if (glass[key] is System.Windows.Media.Brush b)
            {
                var c = b.Clone();          // não congelado: dá para mudar a opacidade depois
                c.Opacity = GlassOpacity;
                glass[key] = c;
                LiveBrushes.Add(c);
            }
    }

    // Fundos que ficam translúcidos (o texto e os ícones continuam 100%).
    private static readonly string[] SeeThrough = { "WindowBg", "BannerBg", "BannerShadow" };

    public static bool IsDark { get; private set; } = true;

    public static event Action? Changed;

    public static void Initialize(string mode, bool glass = false, double glassOpacity = 0.62, bool glassBlur = true)
    {
        GlassBlur = glassBlur;
        Glass = glass;
        WindowGlassAtStartup = glass;
        GlassOpacity = Math.Clamp(glassOpacity, 0.25, 1);
        SystemEvents.UserPreferenceChanged += (_, e) =>
        {
            if (e.Category is UserPreferenceCategory.General or UserPreferenceCategory.Color && _mode == "sistema")
                Application.Current?.Dispatcher.InvokeAsync(() => Apply(_mode));
        };
        Apply(mode);
    }

    public static void Apply(string mode)
    {
        _mode = mode;
        bool dark = mode switch
        {
            "claro" => false,
            "escuro" => true,
            _ => !SystemUsesLightTheme(),
        };

        var resources = Application.Current.Resources.MergedDictionaries;
        var existing = resources.FirstOrDefault(d => d.Source is { } s && (s.OriginalString.EndsWith("Dark.xaml") || s.OriginalString.EndsWith("Light.xaml")));
        var palette = new ResourceDictionary { Source = dark ? DarkUri : LightUri };
        if (existing is null) resources.Insert(0, palette);
        else resources[resources.IndexOf(existing)] = palette;

        // vidro por cima da paleta (sai e entra junto com a troca de claro/escuro)
        var oldGlass = resources.FirstOrDefault(d => d.Source is { } g && g.OriginalString.Contains("Glass"));
        if (oldGlass is not null) resources.Remove(oldGlass);
        if (Glass)
        {
            int at = resources.IndexOf(palette) + 1;
            var glass = new ResourceDictionary { Source = dark ? GlassDarkUri : GlassLightUri };
            Tint(glass);
            resources.Insert(at, glass);
        }

        ApplyFloatLayout();
        bool changed = dark != IsDark;
        IsDark = dark;
        foreach (Window w in Application.Current.Windows) WindowEffects.ApplyFrame(w, dark);
        if (changed) Log.Info("Tema", dark ? "Escuro" : "Claro");
        Changed?.Invoke();
    }

    private static bool SystemUsesLightTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int value && value == 1;
        }
        catch
        {
            return false;
        }
    }
}
