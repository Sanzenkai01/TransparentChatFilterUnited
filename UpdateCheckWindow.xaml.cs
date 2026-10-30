using System.Windows;

namespace TransparentChatFilter;

public partial class UpdateCheckWindow : Window
{
    public UpdateCheckWindow(string status)
    {
        InitializeComponent();
        StatusText.Text = status;
    }

    private void TitleBar_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.ChangedButton == System.Windows.Input.MouseButton.Left)
        {
            DragMove();
        }
    }
}
