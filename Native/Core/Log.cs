using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace Native.Core;

/// <summary>
/// Registro em arquivo, um por dia, em %LocalAppData%\Native\logs.
/// Por privacidade, nunca gravamos o conteúdo de notificações ou mensagens: só estados e erros.
/// </summary>
public static class Log
{
    private static readonly object Gate = new();

    public static string Folder { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Native", "logs");

    public static string CurrentFile => Path.Combine(Folder, $"native-{DateTime.Now:yyyy-MM-dd}.log");

    public static void Info(string area, string message) => Write("INFO ", area, message);
    public static void Warn(string area, string message) => Write("AVISO", area, message);

    public static void Error(string area, string message, Exception? exception = null) =>
        Write("ERRO ", area, exception is null ? message : $"{message} | {exception.GetType().Name}: {FirstLine(exception.Message)} (0x{exception.HResult:X8})");

    public static string FirstLine(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "erro desconhecido";
        return text.Split('\n')[0].Trim();
    }

    /// <summary>Apaga registros com mais de <paramref name="keepDays"/> dias.</summary>
    public static void Prune(int keepDays = 7)
    {
        try
        {
            if (!Directory.Exists(Folder)) return;
            foreach (var file in Directory.GetFiles(Folder, "native-*.log"))
                if (File.GetLastWriteTime(file) < DateTime.Now.AddDays(-keepDays)) File.Delete(file);
        }
        catch
        {
            // Limpeza é opcional.
        }
    }

    /// <summary>As últimas linhas do registro de hoje, para colar num pedido de ajuda.</summary>
    public static string Tail(int maxLines = 200)
    {
        try
        {
            lock (Gate)
            {
                if (!File.Exists(CurrentFile)) return "";
                var lines = File.ReadAllLines(CurrentFile);
                return string.Join(Environment.NewLine, lines.Skip(Math.Max(0, lines.Length - maxLines)));
            }
        }
        catch
        {
            return "";
        }
    }

    private static void Write(string level, string area, string message)
    {
        var line = $"{DateTime.Now:HH:mm:ss.fff} {level} [{area}] {message}";
        Debug.WriteLine(line);
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Folder);
                File.AppendAllText(CurrentFile, line + Environment.NewLine);
            }
        }
        catch
        {
            // Sem disco ou sem permissão: o app continua sem registro.
        }
    }
}
