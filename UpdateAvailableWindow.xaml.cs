using System.Windows;
using System.Windows.Input;

namespace TransparentChatFilter;

internal enum UpdatePromptChoice
{
    Later,
    Update,
    SkipVersion
}

public partial class UpdateAvailableWindow : Window
{
    internal UpdateAvailableWindow(AvailableUpdate update)
    {
        InitializeComponent();
        VersionText.Text = $"A versão {update.Version} está disponível.";
        ReleaseNotesText.Text = string.IsNullOrWhiteSpace(update.ReleaseNotes)
            ? "Esta release não publicou notas da versão."
            : update.ReleaseNotes;
    }

    internal UpdatePromptChoice Choice { get; private set; } = UpdatePromptChoice.Later;

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            DragMove();
        }
    }

    private void AcceptButton_Click(object sender, RoutedEventArgs e)
    {
        Choice = UpdatePromptChoice.Update;
        DialogResult = true;
    }

    private void DeclineButton_Click(object sender, RoutedEventArgs e)
    {
        Choice = UpdatePromptChoice.Later;
        DialogResult = false;
    }

    private void SkipVersionButton_Click(object sender, RoutedEventArgs e)
    {
        Choice = UpdatePromptChoice.SkipVersion;
        DialogResult = false;
    }
}
