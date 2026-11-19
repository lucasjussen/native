using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Native.ViewModels;

namespace Native.Views;

/// <summary>
/// Aba Arquivos. A área "Arraste aqui" tem três estados:
/// parada (cinza), armada (algo está sendo arrastado sobre a janela: acende em azul e pede "Solte aqui")
/// e quente (o cursor está em cima dela: preenche e o ícone cresce). Só ela aceita o arquivo.
/// </summary>
public partial class FilesView : UserControl
{
    private bool _armed, _hot;

    public FilesView()
    {
        InitializeComponent();
    }

    private FilesViewModel? Vm => DataContext as FilesViewModel;

    public void SetArmed(bool armed)
    {
        _armed = armed;
        if (!armed) _hot = false;
        if (armed) Scroller.ScrollToTop();   // a área está no topo; se a aba estava rolada, ela some
        Paint();
    }

    private void Paint()
    {
        var accent = (Brush)FindResource("Accent");
        ZoneStroke.Stroke = _armed ? accent : (Brush)FindResource("LabelTertiary");
        ZoneStroke.StrokeThickness = _armed ? 2 : 1.5;
        ZoneStroke.StrokeDashArray = _hot ? null : new DoubleCollection { 5, 4 };
        Fade(ZoneTint, _hot ? 0.22 : _armed ? 0.10 : 0);
        Grow(_hot ? 1.18 : _armed ? 1.06 : 1);
        ZoneTitle.Text = _hot ? "Solte para mandar ao iPhone" : _armed ? "Solte aqui" : "Arraste aqui para mandar ao iPhone";
        ZoneTitle.Foreground = _armed ? accent : (Brush)FindResource("LabelPrimary");
        ZoneButtons.Visibility = _armed ? Visibility.Collapsed : Visibility.Visible;
        ZoneHint.Visibility = _armed ? Visibility.Visible : Visibility.Collapsed;
    }

    private static readonly Duration Quick = new(TimeSpan.FromMilliseconds(160));
    private static readonly IEasingFunction Ease = new CubicEase { EasingMode = EasingMode.EaseOut };

    private static void Fade(UIElement el, double to) =>
        el.BeginAnimation(OpacityProperty, new DoubleAnimation(to, Quick) { EasingFunction = Ease });

    private void Grow(double to)
    {
        var a = new DoubleAnimation(to, Quick) { EasingFunction = Ease };
        ZoneIconScale.BeginAnimation(ScaleTransform.ScaleXProperty, a);
        ZoneIconScale.BeginAnimation(ScaleTransform.ScaleYProperty, a);
    }

    private static bool Accepts(DragEventArgs e) =>
        e.Data.GetDataPresent(DataFormats.FileDrop) || e.Data.GetDataPresent(DataFormats.UnicodeText) || e.Data.GetDataPresent(DataFormats.Text);

    private void Zone_DragOver(object sender, DragEventArgs e)
    {
        bool ok = Accepts(e);
        e.Effects = ok ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
        if (ok && !_hot) { _hot = true; _armed = true; Paint(); }
    }

    private void Zone_DragLeave(object sender, DragEventArgs e)
    {
        // DragLeave também vem ao passar sobre o ícone/texto de dentro: confere se saiu mesmo.
        var p = e.GetPosition(DropZone);
        if (p.X > 0 && p.Y > 0 && p.X < DropZone.ActualWidth && p.Y < DropZone.ActualHeight) return;
        _hot = false;
        Paint();
    }

    private void Zone_Drop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        if (Vm is not null)
        {
            if (e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0) Vm.Send(files);
            else if (e.Data.GetData(DataFormats.UnicodeText) is string text && text.Trim().Length > 0) Vm.SendText(text);
        }
        _hot = false;
        if (Window.GetWindow(this) is MainWindow w) w.DropCompleted();
        else SetArmed(false);
    }
}
