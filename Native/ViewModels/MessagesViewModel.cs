using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using Native.Bluetooth;
using Native.Core;
using Native.Models;
using Native.Protocol;

namespace Native.ViewModels;

/// <summary>Aba Mensagens: lista de conversas e a conversa aberta, como no app Mensagens.</summary>
public sealed class MessagesViewModel : ObservableObject
{
    private readonly MessageService _service;
    private readonly PeopleDirectory _people;
    private ConversationSummary? _openSummary;
    private DateTime _lastLoad = DateTime.MinValue;

    public MessagesViewModel(MessageService service, PeopleDirectory people)
    {
        _service = service;
        _people = people;

        RefreshCommand = new RelayCommand(async () => await LoadAsync(force: true));
        OpenCommand = new RelayCommand(async p => { if (p is ConversationItem c) await OpenAsync(c); });
        BackCommand = new RelayCommand(() => { IsThreadOpen = false; IsComposingNew = false; });
        SendCommand = new RelayCommand(async () => await SendAsync(), _ => CanSend);
        NewMessageCommand = new RelayCommand(() =>
        {
            IsComposingNew = true;
            IsThreadOpen = true;
            ThreadTitle = "Nova mensagem";
            ThreadSubtitle = "";
            NewRecipient = "";
            Bubbles.Clear();
            Status = "";
        });
    }

    public ObservableCollection<ConversationItem> Conversations { get; } = new();
    public ObservableCollection<MessageBubble> Bubbles { get; } = new();

    public ICommand RefreshCommand { get; }
    public ICommand OpenCommand { get; }
    public ICommand BackCommand { get; }
    public ICommand SendCommand { get; }
    public ICommand NewMessageCommand { get; }

    private bool _isLoading;
    public bool IsLoading { get => _isLoading; private set { if (Set(ref _isLoading, value)) Raise(nameof(ShowEmpty)); } }

    private string _error = "";
    public string Error { get => _error; private set { if (Set(ref _error, value)) { Raise(nameof(HasError)); Raise(nameof(ShowEmpty)); } } }
    public bool HasError => Error.Length > 0;
    public bool ShowEmpty => !IsLoading && !HasError && Conversations.Count == 0 && _lastLoad != DateTime.MinValue;

    private bool _isThreadOpen;
    public bool IsThreadOpen { get => _isThreadOpen; private set => Set(ref _isThreadOpen, value); }

    private bool _isComposingNew;
    public bool IsComposingNew { get => _isComposingNew; private set => Set(ref _isComposingNew, value); }

    private string _newRecipient = "";
    public string NewRecipient { get => _newRecipient; set { if (Set(ref _newRecipient, value)) Raise(nameof(CanSend)); } }

    private string _threadTitle = "";
    public string ThreadTitle { get => _threadTitle; private set => Set(ref _threadTitle, value); }

    private string _threadSubtitle = "";
    public string ThreadSubtitle { get => _threadSubtitle; private set => Set(ref _threadSubtitle, value); }

    private ConversationItem? _openConversation;
    public ConversationItem? OpenConversation { get => _openConversation; private set => Set(ref _openConversation, value); }

    private bool _isThreadLoading;
    public bool IsThreadLoading { get => _isThreadLoading; private set => Set(ref _isThreadLoading, value); }

    private string _composeText = "";
    public string ComposeText { get => _composeText; set { if (Set(ref _composeText, value)) Raise(nameof(CanSend)); } }

    private bool _isSending;
    public bool IsSending { get => _isSending; private set { if (Set(ref _isSending, value)) Raise(nameof(CanSend)); } }

    public bool CanSend => !IsSending && ComposeText.Trim().Length > 0 &&
                           (!IsComposingNew || ResolveRecipient(NewRecipient) is not null);

    private string _status = "";
    public string Status { get => _status; private set { if (Set(ref _status, value)) Raise(nameof(HasStatus)); } }
    public bool HasStatus => Status.Length > 0;

    private string _search = "";
    public string Search
    {
        get => _search;
        set { if (Set(ref _search, value)) ApplyFilter(); }
    }

    private System.Collections.Generic.List<ConversationItem> _all = new();

    /// <summary>Chamado ao abrir a aba e quando chega um SMS/iMessage.</summary>
    public async Task LoadAsync(bool force = false)
    {
        if (IsLoading) return;
        if (!force && (DateTime.Now - _lastLoad).TotalSeconds < 20) return;

        IsLoading = true;
        Error = "";
        try
        {
            var list = await _service.LoadConversationsAsync();
            _lastLoad = DateTime.Now;
            _all = list.Select(ToItem).ToList();
            ApplyFilter();

            if (IsThreadOpen && !IsComposingNew && _openSummary is not null)
            {
                var updated = list.FirstOrDefault(c => c.Key == _openSummary.Key);
                if (updated is not null && updated.When > _openSummary.When) await LoadThreadAsync(updated);
            }
        }
        catch (ClassicException ex)
        {
            Error = ex.Message;
        }
        finally
        {
            IsLoading = false;
            Raise(nameof(ShowEmpty));
        }
    }

    /// <summary>Resposta rápida a partir de uma notificação; a conversa aberta ganha o balão na hora.</summary>
    public async Task ReplyToNotificationAsync(string title, string text)
    {
        await _service.ReplyToAsync(title, text);
        if (IsThreadOpen && OpenConversation is { } open && TextMatch.SameName(open.Name, title))
            Bubbles.Add(new MessageBubble { Text = text, When = DateTime.Now, Outgoing = true, Status = "Enviada" });
        _lastLoad = DateTime.MinValue;
    }

    /// <summary>Abre a conversa com alguém (usado pelo botão "Mensagem" de um contato).</summary>
    public async Task OpenWithAsync(string name, string address)
    {
        if (_lastLoad == DateTime.MinValue) await LoadAsync();
        var key = TextMatch.AddressKey(address);
        var existing = _all.FirstOrDefault(c => c.Key == key);
        if (existing is not null)
        {
            await OpenAsync(existing);
            return;
        }

        NewMessageCommand.Execute(null);
        NewRecipient = address;
        ThreadTitle = name.Length > 0 ? name : address;
    }

    internal void SimulateConversations(System.Collections.Generic.IEnumerable<ConversationSummary> list)
    {
        _lastLoad = DateTime.Now.AddYears(1);   // não tenta buscar no iPhone
        _all = list.Select(ToItem).ToList();
        ApplyFilter();
    }

    private ConversationItem ToItem(ConversationSummary s)
    {
        var item = new ConversationItem
        {
            Key = s.Key,
            Address = s.Address,
            Preview = s.Preview,
            When = s.When,
            Unread = s.Unread,
            LastIsOutgoing = s.LastIsOutgoing,
            Source = s,
            Name = _people.NameFor(s.Address) ?? (s.Name.Length > 0 ? s.Name : s.Address),
        };
        item.Photo = _people.PhotoFor(s.Address);
        return item;
    }

    private void ApplyFilter()
    {
        var term = TextMatch.Normalize(Search);
        var filtered = term.Length == 0
            ? _all
            : _all.Where(c => TextMatch.Normalize(c.Name).Contains(term) || TextMatch.Normalize(c.Preview).Contains(term) ||
                              (TextMatch.Digits(term).Length >= 3 && TextMatch.Digits(c.Address).Contains(TextMatch.Digits(term)))).ToList();

        Conversations.Clear();
        foreach (var c in filtered) Conversations.Add(c);
        Raise(nameof(ShowEmpty));
    }

    private async Task OpenAsync(ConversationItem item)
    {
        if (item.Source is not ConversationSummary summary) return;
        IsComposingNew = false;
        OpenConversation = item;
        ThreadTitle = item.Name;
        ThreadSubtitle = item.Name == item.Address ? "" : item.Address;
        Bubbles.Clear();
        Status = "";
        IsThreadOpen = true;
        await LoadThreadAsync(summary);
    }

    private async Task LoadThreadAsync(ConversationSummary summary)
    {
        _openSummary = summary;
        IsThreadLoading = true;
        try
        {
            var messages = await _service.LoadThreadAsync(summary);
            Bubbles.Clear();
            DateTime previous = DateTime.MinValue;
            foreach (var m in messages)
            {
                Bubbles.Add(new MessageBubble
                {
                    Text = m.Text,
                    When = m.When,
                    Outgoing = m.Outgoing,
                    ShowTimestamp = previous == DateTime.MinValue || (m.When - previous).TotalMinutes > 60 || m.When.Date != previous.Date,
                });
                previous = m.When;
            }
        }
        catch (ClassicException ex)
        {
            Status = ex.Message;
        }
        finally
        {
            IsThreadLoading = false;
        }
    }

    private async Task SendAsync()
    {
        var text = ComposeText.Trim();
        if (text.Length == 0) return;

        string name, address;
        if (IsComposingNew)
        {
            var target = ResolveRecipient(NewRecipient);
            if (target is null) return;
            (name, address) = target.Value;
        }
        else if (OpenConversation is { } open)
        {
            (name, address) = (open.Name, open.Address);
        }
        else return;

        var bubble = new MessageBubble { Text = text, When = DateTime.Now, Outgoing = true, Status = "Enviando…" };
        bubble.ShowTimestamp = Bubbles.Count == 0 || (bubble.When - Bubbles[^1].When).TotalMinutes > 60;
        Bubbles.Add(bubble);
        ComposeText = "";
        IsSending = true;
        Status = "";
        try
        {
            await _service.SendAsync(name, address, text);
            bubble.Status = "Enviada";
            if (IsComposingNew)
            {
                IsComposingNew = false;
                ThreadTitle = name;
                ThreadSubtitle = name == address ? "" : address;
                OpenConversation = new ConversationItem { Key = TextMatch.AddressKey(address), Address = address, Name = name };
            }
            _lastLoad = DateTime.MinValue;   // a lista será atualizada na próxima abertura
        }
        catch (ClassicException ex)
        {
            bubble.Status = "Não enviada";
            bubble.Failed = true;
            Status = ex.Message;
            ComposeText = text;
        }
        finally
        {
            IsSending = false;
        }
    }

    /// <summary>Aceita número, e-mail ou nome de contato da agenda.</summary>
    private (string Name, string Address)? ResolveRecipient(string input)
    {
        input = input.Trim();
        if (input.Length == 0) return null;
        if (TextMatch.IsEmail(input) || (TextMatch.LooksLikePhone(input) && TextMatch.Digits(input).Length >= 8))
            return (_people.NameFor(input) ?? input, input);

        var contact = _people.Service.Find(input);
        var address = contact?.Phones.FirstOrDefault() ?? contact?.Emails.FirstOrDefault();
        return contact is not null && address is not null ? (contact.Name, address) : null;
    }
}
