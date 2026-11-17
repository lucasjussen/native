using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Native.Core;
using Native.Protocol;

namespace Native.Bluetooth;

public sealed class ContactRecord
{
    public string Name { get; set; } = "";
    public List<string> Phones { get; set; } = new();
    public List<string> PhoneKinds { get; set; } = new();
    public List<string> Emails { get; set; } = new();
    public string? PhotoFile { get; set; }
}

public sealed record CallRecord(string Name, string Number, string Kind, DateTime When);

/// <summary>
/// Agenda e histórico de ligações do iPhone pelo Bluetooth clássico (PBAP).
/// A agenda fica guardada no PC (com fotos) para os avatares funcionarem mesmo sem conexão.
/// </summary>
public sealed class ContactService : IDisposable
{
    private const int PageSize = 50;

    private readonly Func<ulong?> _preferredAddress;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _folder;
    private readonly string _photoFolder;
    private readonly string _indexFile;
    private List<ContactRecord> _contacts = new();
    private Dictionary<string, ContactRecord> _byAddress = new();
    private Dictionary<string, ContactRecord> _byName = new();

    public ContactService(string dataFolder, Func<ulong?> preferredAddress)
    {
        _preferredAddress = preferredAddress;
        _folder = Path.Combine(dataFolder, "contatos");
        _photoFolder = Path.Combine(_folder, "fotos");
        _indexFile = Path.Combine(_folder, "contatos.json");
        Load();
    }

    public string NameFilter { get; set; } = "iPhone";
    public IReadOnlyList<ContactRecord> Contacts => _contacts;
    public DateTime? LastSync { get; private set; }

    public event Action? ContactsChanged;

    public ContactRecord? Find(string? nameOrAddress)
    {
        if (string.IsNullOrWhiteSpace(nameOrAddress)) return null;
        if (TextMatch.IsEmail(nameOrAddress) || TextMatch.LooksLikePhone(nameOrAddress))
            return _byAddress.TryGetValue(TextMatch.AddressKey(nameOrAddress), out var byAddress) ? byAddress : null;
        return _byName.TryGetValue(TextMatch.Normalize(nameOrAddress), out var byName) ? byName : null;
    }

    public string? PhotoPath(ContactRecord? contact) =>
        contact?.PhotoFile is { } file && File.Exists(Path.Combine(_photoFolder, file)) ? Path.Combine(_photoFolder, file) : null;

    /// <summary>Baixa a agenda inteira do iPhone (com fotos). Pode levar alguns segundos.</summary>
    public async Task<int> SyncContactsAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            using var session = await OpenAsync();
            int total = await session.Pbap.GetSizeAsync(PbapConstants.Contacts, ct);
            var entries = new List<VCardEntry>();
            // Baixa em fatias: alguns iPhones não informam o total, então para quando vier uma fatia incompleta.
            for (int offset = 0; offset < 20000; offset += PageSize)
            {
                var text = await session.Pbap.PullAsync(PbapConstants.Contacts, PbapConstants.FilterWithPhoto, PageSize, offset, ct);
                var page = VCardParser.Parse(text);
                entries.AddRange(page);
                if (page.Count == 0 || page.Count < PageSize) break;
                if (total > 0 && offset + PageSize >= total) break;
            }

            var records = new List<ContactRecord>();
            Directory.CreateDirectory(_photoFolder);
            var keepPhotos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var e in entries)
            {
                // O primeiro cartão do PBAP é o "meu cartão" (o dono do iPhone), muitas vezes vazio.
                if (e.DisplayName.Length == 0) continue;
                var record = new ContactRecord
                {
                    Name = e.DisplayName,
                    Phones = e.Phones.Select(p => p.Number).ToList(),
                    PhoneKinds = e.Phones.Select(p => p.Kind).ToList(),
                    Emails = e.Emails.ToList(),
                };
                if (e.Photo is { Length: > 0 } photo)
                {
                    var file = Convert.ToHexString(SHA1.HashData(photo))[..16] + ".jpg";
                    var path = Path.Combine(_photoFolder, file);
                    if (!File.Exists(path)) await File.WriteAllBytesAsync(path, photo, ct);
                    record.PhotoFile = file;
                    keepPhotos.Add(file);
                }
                records.Add(record);
            }

            foreach (var old in Directory.GetFiles(_photoFolder))
                if (!keepPhotos.Contains(Path.GetFileName(old))) TryDelete(old);

            _contacts = records.OrderBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
            BuildIndex();
            LastSync = DateTime.Now;
            Save();
            Log.Info("PBAP", $"Agenda sincronizada: {_contacts.Count} contatos");
            ContactsChanged?.Invoke();
            return _contacts.Count;
        }
        catch (ObexException ex)
        {
            Log.Warn("PBAP", ex.Message);
            throw new ClassicException(ex.Code is ObexStatus.Forbidden or ObexStatus.Unauthorized
                ? "O iPhone não liberou os contatos. No iPhone: Ajustes > Bluetooth > (i) ao lado do PC > ative \"Sincronizar Contatos\"."
                : $"O iPhone recusou os contatos ({ex.Message}).");
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Ligações recentes (recebidas, feitas e perdidas), mais novas primeiro.</summary>
    public async Task<List<CallRecord>> GetCallHistoryAsync(int max = 60, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            using var session = await OpenAsync();
            var text = await session.Pbap.PullAsync(PbapConstants.CombinedCallHistory, PbapConstants.FilterCallHistory, max, 0, ct);
            return VCardParser.Parse(text)
                .Where(e => e.PrimaryAddress.Length > 0 || e.DisplayName.Length > 0)
                .Select(e =>
                {
                    var number = e.PrimaryAddress;
                    var name = e.FormattedName.Length > 0 || e.FirstName.Length > 0 ? e.DisplayName : Find(number)?.Name ?? number;
                    return new CallRecord(name.Length > 0 ? name : "Número desconhecido", number, e.CallKind ?? "", e.CallTime ?? DateTime.MinValue);
                })
                .OrderByDescending(c => c.When)
                .ToList();
        }
        catch (ObexException ex)
        {
            Log.Warn("PBAP", ex.Message);
            throw new ClassicException(ex.Code is ObexStatus.Forbidden or ObexStatus.Unauthorized
                ? "O iPhone não liberou o histórico. No iPhone: Ajustes > Bluetooth > (i) ao lado do PC > ative \"Sincronizar Contatos\"."
                : $"O iPhone recusou o histórico ({ex.Message}).");
        }
        finally
        {
            _gate.Release();
        }
    }

    // ---------------------------------------------------------------- sessão e cache

    private sealed class Session : IDisposable
    {
        public Session(ObexClient obex)
        {
            Obex = obex;
            Pbap = new PbapClient(obex);
        }

        public ObexClient Obex { get; }
        public PbapClient Pbap { get; }

        public void Dispose()
        {
            try { Obex.DisconnectAsync().Wait(TimeSpan.FromSeconds(3)); } catch { }
            Obex.Dispose();
        }
    }

    private async Task<Session> OpenAsync()
    {
        Services.PhoneLinkGuard.Stop();
        var transport = await RfcommTransport.OpenAsync(PbapConstants.PseService, "os contatos", _preferredAddress(), NameFilter);
        var session = new Session(new ObexClient(transport, TimeSpan.FromSeconds(30)));
        try
        {
            await session.Pbap.ConnectAsync();
            return session;
        }
        catch
        {
            session.Obex.Dispose();
            throw;
        }
    }

    private void BuildIndex()
    {
        var byAddress = new Dictionary<string, ContactRecord>();
        var byName = new Dictionary<string, ContactRecord>();
        foreach (var c in _contacts)
        {
            foreach (var a in c.Phones.Concat(c.Emails))
            {
                var key = TextMatch.AddressKey(a);
                if (key.Length > 0) byAddress.TryAdd(key, c);
            }
            var name = TextMatch.Normalize(c.Name);
            if (name.Length > 0) byName.TryAdd(name, c);
        }
        _byAddress = byAddress;
        _byName = byName;
    }

    private sealed class CacheFile
    {
        public DateTime? LastSync { get; set; }
        public List<ContactRecord> Contacts { get; set; } = new();
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(_indexFile)) return;
            var cache = JsonSerializer.Deserialize<CacheFile>(File.ReadAllText(_indexFile, Encoding.UTF8));
            if (cache is null) return;
            _contacts = cache.Contacts;
            LastSync = cache.LastSync;
            BuildIndex();
        }
        catch (Exception ex)
        {
            Log.Warn("PBAP", $"Cache de contatos ignorado: {Log.FirstLine(ex.Message)}");
        }
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(_folder);
            File.WriteAllText(_indexFile, JsonSerializer.Serialize(new CacheFile { LastSync = LastSync, Contacts = _contacts }), Encoding.UTF8);
        }
        catch (Exception ex)
        {
            Log.Warn("PBAP", $"Não salvei o cache de contatos: {Log.FirstLine(ex.Message)}");
        }
    }

    /// <summary>Apaga a agenda guardada no PC.</summary>
    public void ClearCache()
    {
        _contacts = new List<ContactRecord>();
        BuildIndex();
        LastSync = null;
        try { if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true); } catch { }
        ContactsChanged?.Invoke();
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch { }
    }

    public void Dispose() => _gate.Dispose();
}
