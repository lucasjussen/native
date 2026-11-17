using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using Native.Bluetooth;
using Native.Core;
using Native.Models;
using Native.Protocol;

namespace Native.ViewModels;

/// <summary>Aba Telefone: ligações recentes, contatos e discagem (a ligação acontece no iPhone).</summary>
public sealed class PhoneViewModel : ObservableObject
{
    private readonly CallService _calls;
    private readonly PeopleDirectory _people;
    private List<ContactItem> _allContacts = new();
    private DateTime _lastRecents = DateTime.MinValue;

    public PhoneViewModel(CallService calls, PeopleDirectory people)
    {
        _calls = calls;
        _people = people;
        _people.Service.ContactsChanged += () => System.Windows.Application.Current?.Dispatcher.InvokeAsync(BuildContacts);

        CallCommand = new RelayCommand(async p => await CallAsync(p));
        DialCommand = new RelayCommand(async () => await CallAsync(DialNumber), _ => CallService.Sanitize(DialNumber).Length >= 3);
        SyncContactsCommand = new RelayCommand(async () => await SyncContactsAsync());
        RefreshRecentsCommand = new RelayCommand(async () => await LoadRecentsAsync(force: true));
        ShowRecentsCommand = new RelayCommand(() => ShowContacts = false);
        ShowContactsCommand = new RelayCommand(() => ShowContacts = true);
        KeyCommand = new RelayCommand(p => { if (p is string key) DialNumber += key; });
        BackspaceCommand = new RelayCommand(() => { if (DialNumber.Length > 0) DialNumber = DialNumber[..^1]; });

        BuildContacts();
    }

    public ObservableCollection<CallItem> Recents { get; } = new();
    public ObservableCollection<ContactItem> Contacts { get; } = new();

    public ICommand CallCommand { get; }
    public ICommand DialCommand { get; }
    public ICommand SyncContactsCommand { get; }
    public ICommand RefreshRecentsCommand { get; }
    public ICommand ShowRecentsCommand { get; }
    public ICommand ShowContactsCommand { get; }
    public ICommand KeyCommand { get; }
    public ICommand BackspaceCommand { get; }

    /// <summary>Definido pela tela principal: abre a conversa com o contato.</summary>
    public Func<string, string, Task>? OpenConversation { get; set; }

    private bool _showContacts;
    public bool ShowContacts
    {
        get => _showContacts;
        set { if (Set(ref _showContacts, value)) Raise(nameof(ShowRecents)); }
    }
    public bool ShowRecents { get => !ShowContacts; set => ShowContacts = !value; }

    private string _search = "";
    public string Search { get => _search; set { if (Set(ref _search, value)) FilterContacts(); } }

    private string _dialNumber = "";
    public string DialNumber { get => _dialNumber; set { if (Set(ref _dialNumber, value)) Raise(nameof(HasDialNumber)); } }
    public bool HasDialNumber => DialNumber.Length > 0;

    private bool _isBusy;
    public bool IsBusy { get => _isBusy; private set => Set(ref _isBusy, value); }

    private string _status = "";
    public string Status { get => _status; private set { if (Set(ref _status, value)) Raise(nameof(HasStatus)); } }
    public bool HasStatus => Status.Length > 0;

    private bool _statusIsError;
    public bool StatusIsError { get => _statusIsError; private set => Set(ref _statusIsError, value); }

    public bool HasContacts => _allContacts.Count > 0;
    public bool HasNoContacts => _allContacts.Count == 0;
    public bool HasNoRecents => Recents.Count == 0 && _lastRecents != DateTime.MinValue;

    public string ContactsSyncLabel => _people.Service.LastSync is { } when
        ? $"{_allContacts.Count} contatos · atualizado {TimeFormat.Relative(when).ToLowerInvariant()}"
        : "Agenda ainda não sincronizada";

    public async Task LoadRecentsAsync(bool force = false)
    {
        if (IsBusy) return;
        if (!force && (DateTime.Now - _lastRecents).TotalSeconds < 30) return;

        IsBusy = true;
        try
        {
            var history = await _people.Service.GetCallHistoryAsync();
            _lastRecents = DateTime.Now;
            Recents.Clear();
            foreach (var call in history)
            {
                var item = new CallItem { Number = call.Number, Kind = call.Kind, When = call.When, Name = call.Name };
                item.Photo = _people.PhotoFor(call.Number);
                Recents.Add(item);
            }
            if (!StatusIsError) Status = "";
        }
        catch (ClassicException ex)
        {
            ShowStatus(ex.Message, error: true);
        }
        finally
        {
            IsBusy = false;
            Raise(nameof(HasNoRecents));
        }
    }

    internal void SimulateRecents(System.Collections.Generic.IEnumerable<CallRecord> calls)
    {
        _lastRecents = DateTime.Now.AddYears(1);
        Recents.Clear();
        foreach (var call in calls) Recents.Add(new CallItem { Number = call.Number, Kind = call.Kind, When = call.When, Name = call.Name });
        Raise(nameof(HasNoRecents));
    }

    public async Task SyncContactsAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        ShowStatus("Baixando a agenda do iPhone…", error: false);
        try
        {
            int count = await _people.Service.SyncContactsAsync();
            ShowStatus($"{count} contatos sincronizados.", error: false);
        }
        catch (ClassicException ex)
        {
            ShowStatus(ex.Message, error: true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Liga para um número (usado também pelo "Ligar de volta" das notificações).</summary>
    public async Task CallAsync(object? target)
    {
        string number = target switch
        {
            CallItem call => call.Number,
            ContactItem contact => contact.PrimaryNumber,
            string text => text,
            _ => "",
        };
        if (number.Length == 0) return;

        ShowStatus($"Ligando para {DisplayFor(number)} pelo iPhone…", error: false);
        try
        {
            await _calls.DialAsync(number);
            ShowStatus($"Ligação iniciada no iPhone: {DisplayFor(number)}.", error: false);
            DialNumber = "";
        }
        catch (ClassicException ex)
        {
            ShowStatus(ex.Message, error: true);
        }
    }

    public Task MessageAsync(ContactItem contact)
    {
        var address = contact.PrimaryNumber.Length > 0 ? contact.PrimaryNumber : contact.Emails.FirstOrDefault() ?? "";
        return address.Length == 0 || OpenConversation is null ? Task.CompletedTask : OpenConversation(contact.Name, address);
    }

    private string DisplayFor(string number) => _people.NameFor(number) ?? number;

    private void ShowStatus(string text, bool error)
    {
        StatusIsError = error;
        Status = text;
    }

    private void BuildContacts()
    {
        _allContacts = _people.Service.Contacts.Select(c =>
        {
            var item = new ContactItem
            {
                Phones = c.Phones.Select((n, i) => (n, i < c.PhoneKinds.Count ? c.PhoneKinds[i] : "")).ToList(),
                Emails = c.Emails.ToList(),
                SearchKey = TextMatch.Normalize(c.Name) + " " + string.Join(" ", c.Phones.Select(TextMatch.Digits)),
                Name = c.Name,
            };
            item.Photo = _people.Photo(c);
            return item;
        }).ToList();
        FilterContacts();
        Raise(nameof(HasContacts));
        Raise(nameof(HasNoContacts));
        Raise(nameof(ContactsSyncLabel));
    }

    private void FilterContacts()
    {
        var term = TextMatch.Normalize(Search);
        var digits = TextMatch.Digits(Search);
        var filtered = term.Length == 0
            ? _allContacts
            : _allContacts.Where(c => c.SearchKey.Contains(term) || (digits.Length >= 3 && c.SearchKey.Contains(digits)));

        Contacts.Clear();
        foreach (var c in filtered.Take(400)) Contacts.Add(c);
    }
}
