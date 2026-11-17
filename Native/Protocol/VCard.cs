using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Native.Protocol;

public sealed class VCardEntry
{
    public string FormattedName { get; set; } = "";
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    public List<(string Number, string Kind)> Phones { get; } = new();
    public List<string> Emails { get; } = new();
    public byte[]? Photo { get; set; }

    /// <summary>Só no histórico de ligações: RECEIVED, DIALED ou MISSED.</summary>
    public string? CallKind { get; set; }
    public DateTime? CallTime { get; set; }

    public string DisplayName
    {
        get
        {
            if (FormattedName.Length > 0) return FormattedName;
            var composed = $"{FirstName} {LastName}".Trim();
            if (composed.Length > 0) return composed;
            if (Phones.Count > 0) return Phones[0].Number;
            return Emails.Count > 0 ? Emails[0] : "";
        }
    }

    public string PrimaryAddress => Phones.Count > 0 ? Phones[0].Number : Emails.Count > 0 ? Emails[0] : "";
}

/// <summary>Leitor de vCard 2.1 e 3.0, no dialeto que os celulares mandam por Bluetooth.</summary>
public static class VCardParser
{
    public static List<VCardEntry> Parse(string text)
    {
        var entries = new List<VCardEntry>();
        VCardEntry? current = null;

        foreach (var line in Unfold(text))
        {
            int colon = line.IndexOf(':');
            if (colon <= 0) continue;

            var head = line[..colon];
            var value = line[(colon + 1)..];
            var parts = head.Split(';');
            var name = parts[0];
            int dot = name.LastIndexOf('.');           // "item1.TEL" → "TEL"
            if (dot >= 0) name = name[(dot + 1)..];
            name = name.ToUpperInvariant();
            var parameters = parts.Skip(1).Select(p => p.ToUpperInvariant()).ToArray();

            if (name == "BEGIN" && value.Trim().Equals("VCARD", StringComparison.OrdinalIgnoreCase))
            {
                current = new VCardEntry();
                continue;
            }
            if (name == "END" && value.Trim().Equals("VCARD", StringComparison.OrdinalIgnoreCase))
            {
                if (current is not null) entries.Add(current);
                current = null;
                continue;
            }
            if (current is null) continue;

            switch (name)
            {
                case "FN":
                    current.FormattedName = Unescape(DecodeText(value, parameters)).Trim();
                    break;

                case "N":
                {
                    var fields = SplitEscaped(DecodeText(value, parameters), ';');
                    current.LastName = Unescape(fields.ElementAtOrDefault(0) ?? "").Trim();
                    current.FirstName = Unescape(fields.ElementAtOrDefault(1) ?? "").Trim();
                    break;
                }

                case "TEL":
                {
                    var number = value.Trim();
                    if (number.Length > 0) current.Phones.Add((number, PhoneKind(parameters)));
                    break;
                }

                case "EMAIL":
                {
                    var email = DecodeText(value, parameters).Trim();
                    if (email.Length > 0) current.Emails.Add(email);
                    break;
                }

                case "PHOTO":
                    if (parameters.Any(p => p.Contains("BASE64") || p == "ENCODING=B" || p == "B"))
                    {
                        try
                        {
                            var compact = new string(value.Where(c => !char.IsWhiteSpace(c)).ToArray());
                            var bytes = Convert.FromBase64String(compact);
                            if (bytes.Length > 64) current.Photo = bytes;
                        }
                        catch (FormatException)
                        {
                            // Foto corrompida: segue sem ela.
                        }
                    }
                    break;

                case "X-IRMC-CALL-DATETIME":
                    current.CallTime = TimeFormat.ParseCompact(value);
                    current.CallKind = parameters
                        .Select(p => p.StartsWith("TYPE=", StringComparison.Ordinal) ? p[5..] : p)
                        .FirstOrDefault(p => p is "RECEIVED" or "DIALED" or "MISSED");
                    break;
            }
        }

        return entries;
    }

    /// <summary>Junta as linhas quebradas: continuação com espaço (3.0 e base64) e "=" no fim (quoted-printable).</summary>
    private static IEnumerable<string> Unfold(string text)
    {
        var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var current = new StringBuilder();
        bool softBreak = false;

        foreach (var raw in lines)
        {
            if (softBreak)
            {
                current.Append(raw);
            }
            else if (raw.Length > 0 && (raw[0] == ' ' || raw[0] == '\t') && current.Length > 0)
            {
                current.Append(raw, 1, raw.Length - 1);
            }
            else
            {
                if (current.Length > 0) yield return current.ToString();
                current.Clear();
                current.Append(raw);
            }

            softBreak = false;
            if (current.Length > 0 && current[^1] == '=' && IsQuotedPrintable(current))
            {
                current.Length--;
                softBreak = true;
            }
        }

        if (current.Length > 0) yield return current.ToString();
    }

    private static bool IsQuotedPrintable(StringBuilder line)
    {
        var text = line.ToString();
        int colon = text.IndexOf(':');
        return colon > 0 && text[..colon].Contains("QUOTED-PRINTABLE", StringComparison.OrdinalIgnoreCase);
    }

    private static string DecodeText(string value, string[] parameters)
    {
        if (!parameters.Any(p => p.Contains("QUOTED-PRINTABLE"))) return value;

        var bytes = new List<byte>(value.Length);
        for (int i = 0; i < value.Length; i++)
        {
            if (value[i] == '=' && i + 2 < value.Length && IsHex(value[i + 1]) && IsHex(value[i + 2]))
            {
                bytes.Add(Convert.ToByte(value.Substring(i + 1, 2), 16));
                i += 2;
            }
            else
            {
                bytes.AddRange(Encoding.UTF8.GetBytes(value[i].ToString()));
            }
        }

        var charset = parameters.FirstOrDefault(p => p.StartsWith("CHARSET=", StringComparison.Ordinal))?[8..];
        try
        {
            return (charset is null or "UTF-8" ? Encoding.UTF8 : Encoding.GetEncoding(charset)).GetString(bytes.ToArray());
        }
        catch (Exception)
        {
            return Encoding.UTF8.GetString(bytes.ToArray());
        }
    }

    private static bool IsHex(char c) => c is >= '0' and <= '9' or >= 'A' and <= 'F' or >= 'a' and <= 'f';

    private static string PhoneKind(string[] parameters)
    {
        var flat = string.Join(",", parameters);
        if (flat.Contains("CELL") || flat.Contains("IPHONE")) return "celular";
        if (flat.Contains("WORK")) return "trabalho";
        if (flat.Contains("HOME")) return "casa";
        if (flat.Contains("MAIN")) return "principal";
        return "";
    }

    private static List<string> SplitEscaped(string value, char separator)
    {
        var parts = new List<string>();
        var sb = new StringBuilder();
        for (int i = 0; i < value.Length; i++)
        {
            if (value[i] == '\\' && i + 1 < value.Length)
            {
                sb.Append(value[i]).Append(value[i + 1]);
                i++;
            }
            else if (value[i] == separator)
            {
                parts.Add(sb.ToString());
                sb.Clear();
            }
            else
            {
                sb.Append(value[i]);
            }
        }
        parts.Add(sb.ToString());
        return parts;
    }

    private static string Unescape(string value)
    {
        if (!value.Contains('\\')) return value;
        var sb = new StringBuilder(value.Length);
        for (int i = 0; i < value.Length; i++)
        {
            if (value[i] == '\\' && i + 1 < value.Length)
            {
                char next = value[++i];
                sb.Append(next is 'n' or 'N' ? '\n' : next);
            }
            else
            {
                sb.Append(value[i]);
            }
        }
        return sb.ToString();
    }
}
