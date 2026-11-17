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

    private void Pin_Click(object sender, RoutedEventArgs e)
    {
        Topmost = !Topmost;
        PinButton.ToolTip = Topmost ? "Fixada na frente (clique para soltar)" : "Manter sempre na frente";
        PinButton.Opacity = Topmost ? 1.0 : 0.85;
    }

    // ------------------------------------------------------------ soltar arquivos em qualquer lugar da janela

    private static bool CanAccept(DragEventArgs e) =>
        e.Data.GetDataPresent(DataFormats.FileDrop) || e.Data.GetDataPresent(DataFormats.UnicodeText) || e.Data.GetDataPresent(DataFormats.Text);

    private void Window_DragEnter(object sender, DragEventArgs e)
    {
        if (!CanAccept(e) || Vm is null || Vm.ShowSetup) return;
        DropOverlay.Visibility = Visibility.Visible;
    }

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        bool ok = CanAccept(e) && Vm is { ShowSetup: false };
        // Texto arrastado para dentro de uma caixa de texto continua sendo texto normal.
        if (ok && !e.Data.GetDataPresent(DataFormats.FileDrop) && e.OriginalSource is DependencyObject d && IsInTextBox(d)) ok = false;
        e.Effects = ok ? DragDropEffects.Copy : DragDropEffects.None;
        DropOverlay.Visibility = ok ? Visibility.Visible : Visibility.Collapsed;
        e.Handled = true;
    }

    private void Window_DragLeave(object sender, DragEventArgs e)
    {
        // DragLeave dispara ao passar entre elementos filhos; só esconde se o mouse saiu da janela.
        var p = e.GetPosition(this);
        if (p.X <= 0 || p.Y <= 0 || p.X >= ActualWidth || p.Y >= ActualHeight) DropOverlay.Visibility = Visibility.Collapsed;
    }

    private void Window_Drop(object sender, DragEventArgs e)
    {
        DropOverlay.Visibility = Visibility.Collapsed;
        if (Vm is null || Vm.ShowSetup) return;
        if (e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
        {
            Vm.Files.Send(files);
        }
        else if (e.OriginalSource is DependencyObject d && IsInTextBox(d))
        {
            return;   // deixa a caixa de texto receber
        }
        else if (e.Data.GetData(DataFormats.UnicodeText) is string text && text.Trim().Length > 0)
        {
            Vm.Files.SendText(text);
        }
        else return;

        Vm.SelectedTab = AppTab.Files;
        e.Handled = true;
    }

    private static bool IsInTextBox(DependencyObject d)
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
            Key.D5 or Key.NumPad5 or Key.OemComma => AppTab.Settings,
            _ => null,
        };
        if (tab is null) return;
        Vm.SelectedTab = tab.Value;
        e.Handled = true;
    }
}
