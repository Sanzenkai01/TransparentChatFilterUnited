using System.Windows;
using System.Windows.Input;

namespace TransparentChatFilter;

public partial class UpdateAvailableWindow : Window
{
    public UpdateAvailableWindow(Version version)
    {
        InitializeComponent();
        VersionText.Text = $"A versão {version} está disponível.";
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            DragMove();
        }
    }

    private void AcceptButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }

    private void DeclineButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
