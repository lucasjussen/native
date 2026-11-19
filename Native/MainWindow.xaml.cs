using System;
using System.Windows;
using System.Windows.Input;
using Native.Services;
using Native.ViewModels;

namespace Native;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) => WindowEffects.ApplyDarkRounded(this);
    }

    private MainViewModel? Vm => DataContext as MainViewModel;

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void Maximize_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    protected override void OnStateChanged(EventArgs e)
    {
        base.OnStateChanged(e);
        bool max = WindowState == WindowState.Maximized;
        // Maximizada sem a moldura do Windows, a janela passa um pouco da tela: compensa a borda.
        RootGrid.Margin = max ? new Thickness(7) : new Thickness(0);
        MaxButton.Content = max ? "\uE923" : "\uE922";
        MaxButton.ToolTip = max ? "Restaurar" : "Maximizar";
    }

    // ------------------------------------------------------------ arrastar arquivos
    // A janela não aceita o arquivo em qualquer lugar: ela só abre a aba Arquivos e acende a área
    // "Solte aqui". Quem recebe é a própria área (FilesView); fora dela o cursor mostra "proibido".

    private AppTab? _tabBeforeDrag;
    private bool _dragArmed;
    private DateTime _lastDragEvent;
    private System.Windows.Threading.DispatcherTimer? _dragWatch;

    // Se o usuário solta fora da área (ou aperta Esc), o Windows não avisa a janela de forma confiável.
    // Enquanto há arraste, chegam DragOver o tempo todo; quando param, a área volta ao normal.
    private void Window_PreviewDragOver(object sender, DragEventArgs e)
    {
        _lastDragEvent = DateTime.UtcNow;
        if (!_dragArmed) Window_DragEnter(sender, e);
    }

    private void WatchDrag()
    {
        _lastDragEvent = DateTime.UtcNow;
        _dragWatch ??= new System.Windows.Threading.DispatcherTimer(TimeSpan.FromMilliseconds(200),
            System.Windows.Threading.DispatcherPriority.Background, (_, _) =>
            {
                if (_dragArmed && (DateTime.UtcNow - _lastDragEvent).TotalMilliseconds > 700) Disarm(restoreTab: false);
                if (!_dragArmed) _dragWatch!.Stop();
            }, Dispatcher);
        _dragWatch.Start();
    }

    private static bool IsFileDrag(DragEventArgs e) =>
        e.Data.GetDataPresent(DataFormats.FileDrop) || e.Data.GetDataPresent(DataFormats.UnicodeText) || e.Data.GetDataPresent(DataFormats.Text);

    private void Window_DragEnter(object sender, DragEventArgs e)
    {
        if (_dragArmed || Vm is null || Vm.ShowSetup || !IsFileDrag(e)) return;
        // Texto arrastado de dentro de uma caixa de texto não arma nada.
        if (!e.Data.GetDataPresent(DataFormats.FileDrop) && e.OriginalSource is DependencyObject d && IsInTextBox(d)) return;
        _dragArmed = true;
        if (Vm.SelectedTab != AppTab.Files) { _tabBeforeDrag = Vm.SelectedTab; Vm.SelectedTab = AppTab.Files; }
        else _tabBeforeDrag = null;
        FilesPage.SetArmed(true);
        WatchDrag();
    }

    // Chega aqui só o que NÃO caiu na área (ela marca o evento como tratado).
    private void Window_DragOver(object sender, DragEventArgs e)
    {
        if (e.OriginalSource is DependencyObject d && IsInTextBox(d) && !e.Data.GetDataPresent(DataFormats.FileDrop)) return;
        e.Effects = DragDropEffects.None;
        e.Handled = true;
    }

    private void Window_DragLeave(object sender, DragEventArgs e)
    {
        // DragLeave também dispara ao passar entre elementos; só desarma quando o mouse sai da janela.
        var p = e.GetPosition(this);
        if (p.X > 0 && p.Y > 0 && p.X < ActualWidth && p.Y < ActualHeight) return;
        Disarm(restoreTab: true);
    }

    private void Window_Drop(object sender, DragEventArgs e) => Disarm(restoreTab: true);

    /// <summary>Chamado pela área quando o arquivo foi entregue (fica na aba Arquivos).</summary>
    public void DropCompleted() => Disarm(restoreTab: false);

    private void Disarm(bool restoreTab)
    {
        if (!_dragArmed) return;
        _dragArmed = false;
        FilesPage.SetArmed(false);
        if (restoreTab && _tabBeforeDrag is { } t && Vm is not null) Vm.SelectedTab = t;
        _tabBeforeDrag = null;
    }

    internal static bool IsInTextBox(DependencyObject d)
    {
        for (var cur = d; cur is not null; cur = cur is System.Windows.Media.Visual ? System.Windows.Media.VisualTreeHelper.GetParent(cur) : LogicalTreeHelper.GetParent(cur))
            if (cur is System.Windows.Controls.Primitives.TextBoxBase) return true;
        return false;
    }

    /// <summary>Atalhos de teclado das abas: Ctrl+1…5, como nos apps do Mac.</summary>
    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        if (Vm is null || Keyboard.Modifiers != ModifierKeys.Control) return;

        // Ctrl+V na aba Arquivos (fora de caixa de texto) manda o que estiver copiado para o iPhone.
        if (e.Key == Key.V && Vm.IsFilesTab && Keyboard.FocusedElement is not System.Windows.Controls.Primitives.TextBoxBase)
        {
            Vm.Files.Paste();
            e.Handled = true;
            return;
        }

        AppTab? tab = e.Key switch
        {
            Key.D1 or Key.NumPad1 => AppTab.Notifications,
            Key.D2 or Key.NumPad2 => AppTab.Messages,
            Key.D3 or Key.NumPad3 => AppTab.Phone,
            Key.D4 or Key.NumPad4 => AppTab.Files,
            Key.D5 or Key.NumPad5 or Key.OemComma => AppTab.Settings,   // Ajustes agora é a engrenagem
            _ => null,
        };
        if (tab is null) return;
        Vm.SelectedTab = tab.Value;
        e.Handled = true;
    }
}
