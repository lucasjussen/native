using System.Windows;
using System.Windows.Controls;
using Native.Models;
using Native.ViewModels;

namespace Native.Views;

public partial class PhoneView : UserControl
{
    public PhoneView()
    {
        InitializeComponent();
    }

    private void Message_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is PhoneViewModel vm && (sender as FrameworkElement)?.DataContext is ContactItem contact)
            _ = vm.MessageAsync(contact);
    }
}
