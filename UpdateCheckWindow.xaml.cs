using System.Windows;
using System.Threading;

namespace TransparentChatFilter;

public partial class UpdateCheckWindow : Window
{
    private readonly CancellationTokenSource _cancellationSource;

    public UpdateCheckWindow(string status, CancellationTokenSource cancellationSource)
    {
        _cancellationSource = cancellationSource;
        InitializeComponent();
        StatusText.Text = status;
    }

    public void ReportProgress(double progress)
    {
        DownloadProgress.Value = Math.Clamp(progress * 100, 0, 100);
        ProgressText.Text = $"{DownloadProgress.Value:0}%";
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        CancelButton.IsEnabled = false;
        StatusText.Text = "Cancelando download...";
        _cancellationSource.Cancel();
    }

    private void TitleBar_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.ChangedButton == System.Windows.Input.MouseButton.Left)
        {
            DragMove();
        }
    }
}
