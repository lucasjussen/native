using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using Microsoft.Win32;

namespace Native.Services;

public sealed class AppSettings
{
    public static string Folder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Native");

    private static string FilePath => Path.Combine(Folder, "settings.json");

    public HashSet<string> MutedApps { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public bool ShowBanners { get; set; } = true;
    public bool PlaySound { get; set; } = true;
    public bool AutoCopyCodes { get; set; } = true;
    public bool DoNotDisturb { get; set; }
    public bool AirPlayAlwaysOn { get; set; }
    public bool SyncContacts { get; set; } = true;
    public double? PipLeft { get; set; }
    public double? PipTop { get; set; }

    // compartilhamento de arquivos
    public string? ShareToken { get; set; }
    public int? SharePort { get; set; }
    public bool SharePaired { get; set; }
    public string? ShareFolder { get; set; }

    /// <summary>"sistema", "claro" ou "escuro".</summary>
    public string Theme { get; set; } = "sistema";

    /// <summary>Já conectou pelo menos uma vez (o guia de conexão não abre mais sozinho).</summary>
    public bool SetupCompleted { get; set; }
    public string? UxPlayPath { get; set; }

    /// <summary>No modo demonstração nada é gravado, para não bagunçar os ajustes reais.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool ReadOnly { get; set; }

    /// <summary>
    /// O app se chamava "Continuidade" até a versão 1.2. Traz ajustes, agenda guardada e registros
    /// da pasta antiga para não perder o pareamento nem as preferências.
    /// </summary>
    public static void MigrateFromOldName()
    {
        try
        {
            var old = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Continuidade");
            if (!Directory.Exists(old) || Directory.Exists(Folder)) return;
            Directory.Move(old, Folder);
            var oldLogs = Path.Combine(Folder, "logs");
            if (Directory.Exists(oldLogs))
                foreach (var f in Directory.GetFiles(oldLogs, "continuidade-*.log"))
                    File.Move(f, Path.Combine(oldLogs, "native-" + Path.GetFileName(f)["continuidade-".Length..]), overwrite: true);
        }
        catch
        {
            // pasta antiga em uso ou sem permissão: começa do zero, sem travar a abertura
        }
    }

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var s = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath));
                if (s is not null)
                {
                    s.MutedApps = new HashSet<string>(s.MutedApps ?? new HashSet<string>(), StringComparer.OrdinalIgnoreCase);
                    return s;
                }
            }
        }
        catch
        {
            // Arquivo corrompido: volta ao padrão.
        }
        return new AppSettings();
    }

    public void Save()
    {
        if (ReadOnly) return;
        try
        {
            Directory.CreateDirectory(Folder);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
            // Sem permissão de escrita: segue sem salvar.
        }
    }
}

public static class StartupRegistration
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Native";

    public static bool IsEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(ValueName) is not null;
        }
    }

    public static void Set(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled)
            key.SetValue(ValueName, $"\"{Environment.ProcessPath}\" --minimized");
        else
            key.DeleteValue(ValueName, false);
    }
}

/// <summary>O Vincular ao Celular segura o serviço de notificações do iPhone; ele precisa estar fechado.</summary>
public static class PhoneLinkGuard
{
    public static void Stop()
    {
        foreach (var p in Process.GetProcessesByName("PhoneExperienceHost"))
        {
            try { p.Kill(); } catch { }
            finally { p.Dispose(); }
        }
    }
}
