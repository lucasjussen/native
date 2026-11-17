using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Native.Protocol;

/// <summary>Comparação tolerante de nomes, telefones e e-mails.</summary>
public static class TextMatch
{
    /// <summary>Minúsculas, sem acentos e sem espaços repetidos.</summary>
    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "";
        var decomposed = value.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        bool lastSpace = false;
        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
            if (char.IsWhiteSpace(c))
            {
                if (!lastSpace) sb.Append(' ');
                lastSpace = true;
                continue;
            }
            sb.Append(c);
            lastSpace = false;
        }
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }

    public static bool SameName(string? a, string? b)
    {
        var x = Normalize(a);
        return x.Length > 0 && x == Normalize(b);
    }

    public static string Digits(string? value) =>
        value is null ? "" : new string(value.Where(char.IsDigit).ToArray());

    public static bool IsEmail(string? value) => value is not null && value.Contains('@') && !value.Contains(' ');

    public static bool LooksLikePhone(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        if (value.Any(char.IsLetter)) return false;
        return Digits(value).Length >= 3;
    }

    /// <summary>
    /// Dois endereços são o mesmo contato? Telefones comparam pelos últimos 8 dígitos
    /// (ignora +55, DDD com zero e o nono dígito); e-mails comparam sem diferenciar maiúsculas.
    /// </summary>
    public static bool SameAddress(string? a, string? b)
    {
        if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b)) return false;
        if (IsEmail(a) || IsEmail(b)) return string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);

        var x = Digits(a);
        var y = Digits(b);
        if (x.Length == 0 || y.Length == 0) return false;
        if (x.Length < 8 || y.Length < 8) return x == y;
        return x[^8..] == y[^8..];
    }

    /// <summary>Chave estável para agrupar conversas pelo mesmo endereço.</summary>
    public static string AddressKey(string address)
    {
        if (IsEmail(address)) return address.Trim().ToLowerInvariant();
        var digits = Digits(address);
        return digits.Length >= 8 ? digits[^8..] : digits;
    }

    public static string Initials(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "";
        var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(p => char.IsLetter(p[0])).ToArray();
        if (parts.Length == 0) return "";
        if (parts.Length == 1) return char.ToUpperInvariant(parts[0][0]).ToString();
        return $"{char.ToUpperInvariant(parts[0][0])}{char.ToUpperInvariant(parts[^1][0])}";
    }
}

/// <summary>Detecta códigos de verificação (2FA) no texto de uma notificação.</summary>
public static class VerificationCode
{
    private static readonly Regex Keywords = new(
        @"c[óo]digo|code|verifica|senha|token|otp|\bpin\b|autentica|confirma|acesso|login",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex Candidates = new(
        @"(?<!R\$\s?)(?<!\d)(?<!\d[.,:/\-])(\d{3}[- ]\d{3}|\d{4,8})(?!\d|[.,:/]\d|\s?(?:reais|%))",
        RegexOptions.Compiled);

    public static string? Find(string? title, string? subtitle, string? message)
    {
        var all = $"{title} {subtitle} {message}";
        if (!Keywords.IsMatch(all)) return null;

        var source = string.IsNullOrEmpty(message) ? all : message;
        var matches = Candidates.Matches(source)
            .Select(m => new string(m.Value.Where(char.IsDigit).ToArray()))
            .ToList();
        if (matches.Count == 0) return null;
        return matches.FirstOrDefault(m => m.Length == 6) ?? matches[0];
    }
}

public static class TimeFormat
{
    public static string Relative(DateTime time) => Relative(time, DateTime.Now);

    public static string Relative(DateTime time, DateTime now)
    {
        var delta = now - time;
        if (delta.TotalMinutes < 1) return "agora";
        if (delta.TotalMinutes < 60) return $"há {(int)delta.TotalMinutes} min";
        if (time.Date == now.Date) return time.ToString("HH:mm", CultureInfo.InvariantCulture);
        if (time.Date == now.Date.AddDays(-1)) return "Ontem";
        if ((now.Date - time.Date).TotalDays < 7) return Weekday(time.DayOfWeek);
        return time.ToString("dd/MM", CultureInfo.InvariantCulture);
    }

    public static string Clock(double seconds)
    {
        if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds < 0) seconds = 0;
        var t = TimeSpan.FromSeconds(seconds);
        return t.TotalHours >= 1
            ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}"
            : $"{t.Minutes}:{t.Seconds:00}";
    }

    private static string Weekday(DayOfWeek day) => day switch
    {
        DayOfWeek.Monday => "Segunda",
        DayOfWeek.Tuesday => "Terça",
        DayOfWeek.Wednesday => "Quarta",
        DayOfWeek.Thursday => "Quinta",
        DayOfWeek.Friday => "Sexta",
        DayOfWeek.Saturday => "Sábado",
        _ => "Domingo",
    };

    /// <summary>Datas no formato dos perfis Bluetooth: 20261116T101010, com Z ou fuso opcional.</summary>
    public static DateTime? ParseCompact(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        value = value.Trim();
        bool utc = value.EndsWith('Z');
        var core = value.Length >= 15 ? value[..15] : value;
        if (!DateTime.TryParseExact(core, "yyyyMMdd'T'HHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
            return null;
        return utc ? DateTime.SpecifyKind(parsed, DateTimeKind.Utc).ToLocalTime() : parsed;
    }
}
