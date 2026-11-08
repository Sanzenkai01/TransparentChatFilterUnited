using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;

namespace TransparentChatFilter;

public partial class SettingsWindow : Window
{
    public SettingsWindow(MainWindow owner)
    {
        OwnerMainWindow = owner;
        InitializeComponent();
        DataContext = owner.DataContext;
        BindingOperations.SetBinding(this, TopmostProperty, new Binding(nameof(Window.Topmost)) { Source = owner });
    }

    public MainWindow OwnerMainWindow { get; }
    public bool AllowClose { get; set; }

    private void SettingsWindow_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
        }
    }

    private void SettingsWindow_Closing(object? sender, CancelEventArgs e)
    {
        if (AllowClose)
        {
            return;
        }

        e.Cancel = true;
        Hide();
        OwnerMainWindow.SettingsWindowHidden();
    }

    private async void CheckForUpdatesButton_Click(object sender, RoutedEventArgs e)
    {
        CheckForUpdatesButton.IsEnabled = false;
        try
        {
            var app = (App)Application.Current;
            await app.CheckForUpdatesManuallyAsync(OwnerMainWindow);
        }
        finally
        {
            CheckForUpdatesButton.IsEnabled = true;
        }
    }

    private void RemoveFilterChip_Click(object sender, RoutedEventArgs e) => OwnerMainWindow.InvokeSettingsHandler(nameof(RemoveFilterChip_Click), sender, e);
    private void CloseSettingsButton_Click(object sender, RoutedEventArgs e) => OwnerMainWindow.InvokeSettingsHandler(nameof(CloseSettingsButton_Click), sender, e);
    private void ChatPreferencesButton_Click(object sender, RoutedEventArgs e) => OwnerMainWindow.InvokeSettingsHandler(nameof(ChatPreferencesButton_Click), sender, e);
    private void ConnectionsPreferencesButton_Click(object sender, RoutedEventArgs e) => OwnerMainWindow.InvokeSettingsHandler(nameof(ConnectionsPreferencesButton_Click), sender, e);
    private void GeneralPreferencesButton_Click(object sender, RoutedEventArgs e) => OwnerMainWindow.InvokeSettingsHandler(nameof(GeneralPreferencesButton_Click), sender, e);
    private void WidgetsPreferencesButton_Click(object sender, RoutedEventArgs e) => OwnerMainWindow.InvokeSettingsHandler(nameof(WidgetsPreferencesButton_Click), sender, e);
    private void OpenFilterAuthentication_Click(object sender, RoutedEventArgs e) => OwnerMainWindow.InvokeSettingsHandler(nameof(OpenFilterAuthentication_Click), sender, e);
    private void RecentChannelsComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e) => OwnerMainWindow.InvokeSettingsHandler(nameof(RecentChannelsComboBox_SelectionChanged), sender, e);
    private void ConnectButton_Click(object sender, RoutedEventArgs e) => OwnerMainWindow.InvokeSettingsHandler(nameof(ConnectButton_Click), sender, e);
    private void FilterModeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e) => OwnerMainWindow.InvokeSettingsHandler(nameof(FilterModeComboBox_SelectionChanged), sender, e);
    private void SavePreferences_Changed(object sender, RoutedEventArgs e) => OwnerMainWindow.InvokeSettingsHandler(nameof(SavePreferences_Changed), sender, e);
    private void AddFilterChip_Click(object sender, RoutedEventArgs e) => OwnerMainWindow.InvokeSettingsHandler(nameof(AddFilterChip_Click), sender, e);
    private void TestFilter_Click(object sender, RoutedEventArgs e) => OwnerMainWindow.InvokeSettingsHandler(nameof(TestFilter_Click), sender, e);
    private void HighlightColor_TextChanged(object sender, TextChangedEventArgs e) => OwnerMainWindow.InvokeSettingsHandler(nameof(HighlightColor_TextChanged), sender, e);
    private void ChooseHighlightColorButton_Click(object sender, RoutedEventArgs e) => OwnerMainWindow.InvokeSettingsHandler(nameof(ChooseHighlightColorButton_Click), sender, e);
    private void ConnectionModeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e) => OwnerMainWindow.InvokeSettingsHandler(nameof(ConnectionModeComboBox_SelectionChanged), sender, e);
    private void TwitchOpenAuthorizationButton_Click(object sender, RoutedEventArgs e) => OwnerMainWindow.InvokeSettingsHandler(nameof(TwitchOpenAuthorizationButton_Click), sender, e);
    private void TwitchCancelAuthorizationButton_Click(object sender, RoutedEventArgs e) => OwnerMainWindow.InvokeSettingsHandler(nameof(TwitchCancelAuthorizationButton_Click), sender, e);
    private void ShowDiagnosticButton_Click(object sender, RoutedEventArgs e) => OwnerMainWindow.InvokeSettingsHandler(nameof(ShowDiagnosticButton_Click), sender, e);
    private void MaxVisibleMessagesSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) => OwnerMainWindow.InvokeSettingsHandler(nameof(MaxVisibleMessagesSlider_ValueChanged), sender, e);
    private void NotificationSoundComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e) => OwnerMainWindow.InvokeSettingsHandler(nameof(NotificationSoundComboBox_SelectionChanged), sender, e);
    private void NotificationSoundPreviewButton_Click(object sender, RoutedEventArgs e) => OwnerMainWindow.InvokeSettingsHandler(nameof(NotificationSoundPreviewButton_Click), sender, e);
    private void NotificationVolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) => OwnerMainWindow.InvokeSettingsHandler(nameof(NotificationVolumeSlider_ValueChanged), sender, e);
    private void FontSizeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) => OwnerMainWindow.InvokeSettingsHandler(nameof(FontSizeSlider_ValueChanged), sender, e);
    private void OpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) => OwnerMainWindow.InvokeSettingsHandler(nameof(OpacitySlider_ValueChanged), sender, e);
    private void AppearanceSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) => OwnerMainWindow.InvokeSettingsHandler(nameof(AppearanceSlider_ValueChanged), sender, e);
    private void MessageBackgroundColor_TextChanged(object sender, TextChangedEventArgs e) => OwnerMainWindow.InvokeSettingsHandler(nameof(MessageBackgroundColor_TextChanged), sender, e);
    private void ChooseMessageBackgroundColorButton_Click(object sender, RoutedEventArgs e) => OwnerMainWindow.InvokeSettingsHandler(nameof(ChooseMessageBackgroundColorButton_Click), sender, e);
    private void NewMessagesButton_Click(object sender, RoutedEventArgs e) => OwnerMainWindow.InvokeSettingsHandler(nameof(NewMessagesButton_Click), sender, e);
    private void PaletteColorButton_Click(object sender, RoutedEventArgs e) => OwnerMainWindow.InvokeSettingsHandler(nameof(PaletteColorButton_Click), sender, e);
    private void RestoreSectionDefaults_Click(object sender, RoutedEventArgs e) => OwnerMainWindow.InvokeSettingsHandler(nameof(RestoreSectionDefaults_Click), sender, e);
}
