using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using Native.Models;
using Native.Services;
using Native.ViewModels;

namespace Native.Views;

public partial class MessagesView : UserControl
{
    private MessagesViewModel? _vm;

    public MessagesView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Attach(DataContext as MessagesViewModel);
    }

    private void Attach(MessagesViewModel? vm)
    {
        if (_vm is not null)
        {
            _vm.Bubbles.CollectionChanged -= OnBubblesChanged;
            _vm.PropertyChanged -= OnVmChanged;
        }
        _vm = vm;
        if (_vm is null) return;
        _vm.Bubbles.CollectionChanged += OnBubblesChanged;
        _vm.PropertyChanged += OnVmChanged;
    }

    /// <summary>Como no Mensagens: a conversa fica sempre rolada até a última mensagem.</summary>
    private void OnBubblesChanged(object? sender, NotifyCollectionChangedEventArgs e) =>
        Dispatcher.InvokeAsync(() => ThreadScroll.ScrollToEnd(), System.Windows.Threading.DispatcherPriority.Background);

    private void OnVmChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MessagesViewModel.IsThreadOpen) && _vm is { IsThreadOpen: true })
            Dispatcher.InvokeAsync(() => Composer.Focus(), System.Windows.Threading.DispatcherPriority.Input);
    }

    private void Composer_TextChanged(object sender, TextChangedEventArgs e) =>
        ComposeHint.Visibility = Composer.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;

    private void CopyBubble_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is MessageBubble bubble) ClipboardHelper.TrySetText(bubble.Text);
    }
}
