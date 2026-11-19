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

    public static void SetGlass(bool on)
    {
        Glass = on;
        Apply(_mode);
    }

    public static bool IsDark { get; private set; } = true;

    public static event Action? Changed;

    public static void Initialize(string mode, bool glass = false)
    {
        Glass = glass;
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
            resources.Insert(at, new ResourceDictionary { Source = dark ? GlassDarkUri : GlassLightUri });
        }

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
