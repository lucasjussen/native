using System;
using System.Windows;
using System.Windows.Input;
using Native.Models;
using Native.Services;
using Native.ViewModels;

namespace Native;

/// <summary>
/// Mini Native: janelinha fixa por cima de tudo com o iPhone (bateria, conexão), o que está tocando
/// e as últimas notificações. Aceita arquivos arrastados, que vão direto para o iPhone.
/// </summary>
public partial class MiniWindow : Window
{
    private readonly MainViewModel _vm;

    /// <summary>Pedido para abrir a janela completa (opcionalmente numa aba).</summary>
    public event Action<AppTab?>? ExpandRequested;
    public event Action? HideRequested;

    public MiniWindow(MainViewModel vm)
    {
        InitializeComponent();
        if (Array.IndexOf(Environment.GetCommandLineArgs(), "--opaque") >= 0)
        {
            // só para capturas de tela em máquinas que não desenham janelas transparentes
            AllowsTransparency = false;
            Background = System.Windows.Media.Brushes.Black;
        }
        _vm = vm;
        DataContext = vm;
        WindowEffects.RegisterGlass(this);
        SourceInitialized += (_, _) => WindowEffects.ApplyFrame(this, ThemeManager.IsDark);
        Loaded += (_, _) => PlaceOnScreen();
        LocationChanged += (_, _) => { if (IsLoaded) { _vm.MiniLeft = Math.Round(Left); _vm.MiniTop = Math.Round(Top); } };
        SizeChanged += (_, _) => KeepInside();
        // Solto (sem alfinete) ele pode ficar atrás de outras janelas: aparece na barra de tarefas para achar.
        ShowInTaskbar = !vm.MiniPinned;
        vm.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(MainViewModel.MiniPinned)) ShowInTaskbar = !vm.MiniPinned; };
    }

    private void PlaceOnScreen()
    {
        var area = SystemParameters.WorkArea;
        Left = _vm.MiniLeft ?? area.Right - ActualWidth - 4;
        Top = _vm.MiniTop ?? area.Top + 4;
        KeepInside();
    }

    /// <summary>Monitor trocou ou a janela cresceu: traz de volta para dentro da tela.</summary>
    private void KeepInside()
    {
        if (!IsLoaded) return;
        var area = SystemParameters.WorkArea;
        Left = Math.Clamp(Left, area.Left - 12, Math.Max(area.Left, area.Right - ActualWidth + 12));
        Top = Math.Clamp(Top, area.Top - 8, Math.Max(area.Top, area.Bottom - ActualHeight + 16));
    }

    private void Drag(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject d && IsInteractive(d)) return;
        try { DragMove(); } catch (InvalidOperationException) { }
    }

    private static bool IsInteractive(DependencyObject d)
    {
        for (var cur = d; cur is not null; cur = cur is System.Windows.Media.Visual ? System.Windows.Media.VisualTreeHelper.GetParent(cur) : LogicalTreeHelper.GetParent(cur))
        {
            if (cur is System.Windows.Controls.Primitives.ButtonBase) return true;
            if (cur is FrameworkElement { Cursor: not null } fe && fe.Cursor == Cursors.Hand) return true;
            if (cur is MiniWindow) break;
        }
        return false;
    }

    private void Expand_Click(object sender, RoutedEventArgs e) => ExpandRequested?.Invoke(null);

    private void Hide_Click(object sender, RoutedEventArgs e) => HideRequested?.Invoke();

    private void Notification_Click(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject d && IsButton(d)) return;
        ExpandRequested?.Invoke(AppTab.Notifications);
    }

    /// <summary>Pilha fechada com várias: abre. Aberta: fecha. Uma só: abre o Native nas notificações.</summary>
    private void Stack_Click(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject d && IsButton(d)) return;
        if ((sender as FrameworkElement)?.DataContext is not NotificationGroup g) return;
        if (g.Items.Count > 1) g.IsExpanded = !g.IsExpanded;
        else ExpandRequested?.Invoke(AppTab.Notifications);
        e.Handled = true;
    }

    private static bool IsButton(DependencyObject d)
    {
        for (var cur = d; cur is not null && cur is not MiniWindow; cur = cur is System.Windows.Media.Visual ? System.Windows.Media.VisualTreeHelper.GetParent(cur) : LogicalTreeHelper.GetParent(cur))
            if (cur is System.Windows.Controls.Primitives.ButtonBase) return true;
        return false;
    }

    private void Dismiss_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is PhoneNotification n) _vm.DismissItem(n);
        e.Handled = true;
    }

    // ------------------------------------------------------------ soltar arquivos: vão para o iPhone

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        bool ok = e.Data.GetDataPresent(DataFormats.FileDrop) || e.Data.GetDataPresent(DataFormats.UnicodeText);
        e.Effects = ok ? DragDropEffects.Copy : DragDropEffects.None;
        DropOverlay.Visibility = ok ? Visibility.Visible : Visibility.Collapsed;
        e.Handled = true;
    }

    private void Window_DragLeave(object sender, DragEventArgs e)
    {
        var p = e.GetPosition(this);
        if (p.X <= 0 || p.Y <= 0 || p.X >= ActualWidth || p.Y >= ActualHeight) DropOverlay.Visibility = Visibility.Collapsed;
    }

    private void Window_Drop(object sender, DragEventArgs e)
    {
        DropOverlay.Visibility = Visibility.Collapsed;
        if (e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0) _vm.Files.Send(files);
        else if (e.Data.GetData(DataFormats.UnicodeText) is string text && text.Trim().Length > 0) _vm.Files.SendText(text);
        e.Handled = true;
    }
}
