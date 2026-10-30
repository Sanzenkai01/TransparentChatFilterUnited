using System.Windows;

namespace TransparentChatFilter;

public partial class UpdateCheckWindow : Window
{
    public UpdateCheckWindow(string status)
    {
        InitializeComponent();
        StatusText.Text = status;
    }
}
