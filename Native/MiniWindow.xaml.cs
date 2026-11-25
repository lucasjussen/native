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
        SourceInitialized += (_, _) =>
        {
            WindowEffects.ApplyFrame(this, ThemeManager.IsDark);
            // Fica em segundo plano de verdade: fora do Alt+Tab e da barra de tarefas, e clicar nele
            // não tira o foco do que você está fazendo (os botões funcionam do mesmo jeito).
            WindowEffects.MakeNonActivating(this);
        };
        Loaded += (_, _) => PlaceOnScreen();
        LocationChanged += (_, _) => { if (IsLoaded) { _vm.MiniLeft = Math.Round(Left); _vm.MiniTop = Math.Round(Top); } };
        SizeChanged += OnSizeChanged;
        // monitor trocado, resolução nova ou barra de tarefas mudou de lugar/tamanho
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged += OnDisplayChanged;
        Microsoft.Win32.SystemEvents.UserPreferenceChanged += OnDisplayChanged;
        Closed += (_, _) =>
        {
            Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= OnDisplayChanged;
            Microsoft.Win32.SystemEvents.UserPreferenceChanged -= OnDisplayChanged;
        };
    }

    private void OnDisplayChanged(object? sender, EventArgs e) => Dispatcher.BeginInvoke(KeepInside);

    /// <summary>
    /// A altura muda sozinha (conectou/desconectou, começou a tocar música, chegou notificação).
    /// O Windows mantém o topo parado e a janela cresce para baixo — encostada na barra de tarefas, ela
    /// era empurrada para cima e não voltava mais. Agora, se o Mini está na metade de baixo da tela, quem
    /// fica parado é a borda de baixo: ele cresce e encolhe para cima, sempre colado onde você deixou.
    /// </summary>
    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (IsLoaded && e.HeightChanged && e.PreviousSize.Height > 0)
        {
            var area = WindowEffects.WorkAreaOf(this);
            double oldBottom = Top + e.PreviousSize.Height;
            double oldCenter = Top + e.PreviousSize.Height / 2;
            if (oldCenter > area.Top + area.Height / 2)
                Top = oldBottom - e.NewSize.Height;
        }
        KeepInside();
    }

    /// <summary>Margem transparente em volta do cartão (sombra). Com o vidro fosco ela é zero.</summary>
    private Thickness Shadow => (Content as FrameworkElement)?.Margin ?? new Thickness(0);

    /// <summary>Distância que ele "gruda" nas bordas da tela e na barra de tarefas.</summary>
    private const double SnapDistance = 24, Gap = 8;

    private void PlaceOnScreen()
    {
        var area = WindowEffects.WorkAreaOf(this);
        var m = Shadow;
        // primeira vez: canto de baixo à direita, encostado na barra de tarefas
        Left = _vm.MiniLeft ?? area.Right - ActualWidth + m.Right - Gap;
        Top = _vm.MiniTop ?? area.Bottom - ActualHeight + m.Bottom - Gap;
        KeepInside();
    }

    /// <summary>
    /// O cartão nunca passa da área útil da tela: não cobre a barra de tarefas nem sai pela borda.
    /// (Só a sombra transparente pode encostar além da linha.)
    /// </summary>
    private void KeepInside()
    {
        if (!IsLoaded) return;
        var area = WindowEffects.WorkAreaOf(this);
        var m = Shadow;
        double minLeft = area.Left - m.Left, maxLeft = area.Right - ActualWidth + m.Right;
        double minTop = area.Top - m.Top, maxTop = area.Bottom - ActualHeight + m.Bottom;
        Left = Math.Clamp(Left, minLeft, Math.Max(minLeft, maxLeft));
        Top = Math.Clamp(Top, minTop, Math.Max(minTop, maxTop));
    }

    /// <summary>Soltou perto de uma borda (ou da barra de tarefas): encaixa certinho, com um respiro.</summary>
    private void Snap()
    {
        var area = WindowEffects.WorkAreaOf(this);
        var m = Shadow;
        double cardLeft = Left + m.Left, cardTop = Top + m.Top;
        double cardRight = Left + ActualWidth - m.Right, cardBottom = Top + ActualHeight - m.Bottom;

        if (area.Bottom - cardBottom < SnapDistance) Top = area.Bottom - Gap - (ActualHeight - m.Bottom);
        else if (cardTop - area.Top < SnapDistance) Top = area.Top + Gap - m.Top;
        if (area.Right - cardRight < SnapDistance) Left = area.Right - Gap - (ActualWidth - m.Right);
        else if (cardLeft - area.Left < SnapDistance) Left = area.Left + Gap - m.Left;
        KeepInside();
    }

    private void Drag(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject d && IsInteractive(d)) return;
        try { DragMove(); } catch (InvalidOperationException) { return; }
        Snap();   // DragMove só volta quando o botão é solto
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
