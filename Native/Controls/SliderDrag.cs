using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace Native.Controls;

/// <summary>
/// Barra que segue o mouse do começo ao fim do arraste, como no iOS. A bolinha das nossas barras tem
/// tamanho zero (flutua sobre a linha), então o "pegar e arrastar" padrão do WPF não funcionava: a barra
/// só pulava para onde se clicava. Aqui o clique prende o mouse na barra e cada movimento atualiza o valor,
/// mesmo se o cursor sair de cima dela.
/// </summary>
public static class SliderDrag
{
    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled", typeof(bool), typeof(SliderDrag), new PropertyMetadata(false, OnChanged));

    public static bool GetIsEnabled(DependencyObject d) => (bool)d.GetValue(IsEnabledProperty);
    public static void SetIsEnabled(DependencyObject d, bool value) => d.SetValue(IsEnabledProperty, value);

    private static readonly DependencyProperty DraggingProperty = DependencyProperty.RegisterAttached(
        "Dragging", typeof(bool), typeof(SliderDrag), new PropertyMetadata(false));

    private static void OnChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not Slider s || e.NewValue is not true) return;
        // Liga só quando a barra aparece: assim os handlers da própria tela (ex.: "começar a arrastar a música")
        // rodam antes e o primeiro valor já cai no modo certo.
        if (s.IsLoaded) Attach(s);
        else s.Loaded += FirstLoad;
    }

    private static void FirstLoad(object sender, RoutedEventArgs e)
    {
        var s = (Slider)sender;
        s.Loaded -= FirstLoad;
        Attach(s);
    }

    private static void Attach(Slider s)
    {
        // handledEventsToo: o próprio Slider marca o clique como tratado antes de nós
        s.AddHandler(UIElement.PreviewMouseLeftButtonDownEvent, new MouseButtonEventHandler(Down), true);
        s.AddHandler(UIElement.PreviewMouseMoveEvent, new MouseEventHandler(Move), true);
        s.AddHandler(UIElement.PreviewMouseLeftButtonUpEvent, new MouseButtonEventHandler(Up), true);
        s.LostMouseCapture += (_, _) => s.SetValue(DraggingProperty, false);
    }

    private static void Down(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Slider s || !s.IsEnabled) return;
        s.SetValue(DraggingProperty, true);
        s.CaptureMouse();
        Update(s, e);
    }

    private static void Move(object sender, MouseEventArgs e)
    {
        if (sender is not Slider s || !(bool)s.GetValue(DraggingProperty)) return;
        if (e.LeftButton != MouseButtonState.Pressed) { Stop(s); return; }
        Update(s, e);
    }

    private static void Up(object sender, MouseButtonEventArgs e)
    {
        if (sender is Slider s) Stop(s);
    }

    private static void Stop(Slider s)
    {
        s.SetValue(DraggingProperty, false);
        if (s.IsMouseCaptured) s.ReleaseMouseCapture();
    }

    private static void Update(Slider s, MouseEventArgs e)
    {
        if (s.Template?.FindName("PART_Track", s) is not Track track || track.ActualWidth <= 0) return;
        double x = Math.Clamp(e.GetPosition(track).X / track.ActualWidth, 0, 1);
        if (s.IsDirectionReversed) x = 1 - x;
        s.Value = s.Minimum + x * (s.Maximum - s.Minimum);
    }
}
