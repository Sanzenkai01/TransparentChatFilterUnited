using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace TransparentChatFilter;

public partial class ReleaseNotesWindow : Window
{
    public ReleaseNotesWindow(Version version)
    {
        InitializeComponent();
        Title = $"Novidades da versão {version.ToString(3)}";
        VersionTitle.Text = $"Versão {version.ToString(3)}";
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        var animation = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(240))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        DialogBorder.BeginAnimation(OpacityProperty, animation);
        ((TranslateTransform)DialogBorder.RenderTransform).BeginAnimation(
            TranslateTransform.YProperty,
            new DoubleAnimation(12, 0, TimeSpan.FromMilliseconds(280))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            });
    }

    private void DismissButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void AcknowledgeButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }
}
