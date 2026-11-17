using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Native.Controls;
using Native.Models;
using Native.ViewModels;

namespace Native.Views;

public partial class NotificationsView : UserControl
{
    public NotificationsView()
    {
        InitializeComponent();
    }

    private MainViewModel? Vm => DataContext as MainViewModel;

    private void Group_Click(object sender, MouseButtonEventArgs e)
    {
        if (FocusOnShow.IsFromInteractive(e.OriginalSource)) return;   // clique no campo de resposta ou num botão
        if (sender is FrameworkElement { DataContext: NotificationGroup group })
            Vm?.ToggleGroupCommand.Execute(group);
    }

    private static PhoneNotification? ItemOf(object sender) =>
        (sender as FrameworkElement)?.DataContext as PhoneNotification;

    private void CopyText_Click(object sender, RoutedEventArgs e)
    {
        if (ItemOf(sender) is { } n) Vm?.CopyText(n);
    }

    private void CopyCode_Click(object sender, RoutedEventArgs e)
    {
        if (ItemOf(sender) is { } n) Vm?.CopyCode(n);
    }

    private void Mute_Click(object sender, RoutedEventArgs e)
    {
        if (ItemOf(sender) is { } n) Vm?.MuteApp(n.App);
    }

    private void Dismiss_Click(object sender, RoutedEventArgs e)
    {
        if (ItemOf(sender) is { } n) Vm?.DismissItem(n);
    }
}
