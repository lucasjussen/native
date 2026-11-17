using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Native.ViewModels;

namespace Native.Views;

public partial class NowPlayingView : UserControl
{
    /// <summary>No mini player o botão de abrir o próprio mini player some.</summary>
    public static readonly DependencyProperty IsCompactProperty = DependencyProperty.Register(
        nameof(IsCompact), typeof(bool), typeof(NowPlayingView),
        new PropertyMetadata(false, (d, e) => ((NowPlayingView)d).PipButton.Visibility = (bool)e.NewValue ? Visibility.Collapsed : Visibility.Visible));

    public bool IsCompact
    {
        get => (bool)GetValue(IsCompactProperty);
        set => SetValue(IsCompactProperty, value);
    }

    private bool _seeking, _changingVolume;

    public NowPlayingView()
    {
        InitializeComponent();
    }

    private MainViewModel? Vm => DataContext as MainViewModel;

    // Arrastar a barra: segura enquanto o mouse está pressionado e só manda ao soltar.
    private void Seek_Down(object sender, MouseButtonEventArgs e)
    {
        if (Vm is not { CanSeek: true }) return;
        _seeking = true;
        Vm.BeginScrub();
    }

    private void Seek_Up(object sender, RoutedEventArgs e)
    {
        if (!_seeking) return;
        _seeking = false;
        Vm?.CommitScrub();
    }

    private void Volume_Down(object sender, MouseButtonEventArgs e)
    {
        _changingVolume = true;
        Vm?.BeginVolumeChange();
    }

    private void Volume_Up(object sender, RoutedEventArgs e)
    {
        if (!_changingVolume) return;
        _changingVolume = false;
        Vm?.CommitVolume();
    }

    /// <summary>Roda do mouse em qualquer parte do player muda o volume do iPhone.</summary>
    private void Root_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (Vm is null) return;
        if (e.Delta > 0) Vm.VolumeUp();
        else Vm.VolumeDown();
        e.Handled = true;
    }
}
