using System.Diagnostics;
using System.Collections.ObjectModel;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Text.Json;
using Microsoft.Win32;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace TransparentChatFilter;

public partial class MainWindow : Window
{
    private const long MessageNotificationSoundCooldownMilliseconds = 500;
    private const int PublicEmoteImageCacheCapacity = 512;
    private const int PublicBadgeImageCacheCapacity = 256;
    private const int PublicChannelImageCacheCapacity = 128;
    private const int IrcEmoteImageCacheCapacity = 128;
    private const int IrcBadgeImageCacheCapacity = 64;
    private const int IrcChannelImageCacheCapacity = 32;
    private static readonly TimeSpan MessageNotificationRenderDelay = TimeSpan.FromMilliseconds(250);
    private static readonly string[] TwitchVideoResourceFilters =
    [
        "https://usher.ttvnw.net/*",
        "https://*.hls.ttvnw.net/*"
    ];
    private static readonly string[] TwitchTrackingResourceFilters =
    [
        "https://ads.twitch.tv/*",
        "https://*.ads.twitch.tv/*",
        "https://spade.twitch.tv/*",
        "https://countess.twitch.tv/*",
        "https://analytics.twitch.tv/*"
    ];
    private static readonly HttpClient TwitchImageHttpClient = new()
    {
        Timeout = Timeout.InfiniteTimeSpan
    };
    private readonly TwitchChatClient _chatClient = new();
    private readonly TwitchBadgeClient _twitchBadgeClient = new();
    private readonly TwitchChannelClient _twitchChannelClient = new();
    private readonly TwitchOAuthClient _twitchOAuthClient = new();
    private readonly AppSettings _settings;
    private readonly CancellationTokenSource _lifetimeCts = new();
    private readonly List<ChatMessage> _chatHistory = new();
    private readonly BoundedAsyncCache<string, TwitchEmoteImageAsset> _twitchEmoteImageCache = new(PublicEmoteImageCacheCapacity, StringComparer.Ordinal);
    private readonly BoundedAsyncCache<string, ImageSource> _twitchBadgeImageCache = new(PublicBadgeImageCacheCapacity, StringComparer.Ordinal);
    private readonly BoundedAsyncCache<string, ImageSource> _twitchChannelImageCache = new(PublicChannelImageCacheCapacity, StringComparer.Ordinal);
    private TwitchCredentials? _twitchCredentials;
    private CancellationTokenSource? _authorizationCts;
    private Uri? _twitchVerificationUri;
    private int _connectInProgress;
    private bool _isConnected;
    private bool _ircReconnectPending;
    private long _lastBadgeImageErrorReportedUtcTicks;
    private bool _twitchConnectionActive;
    private bool _sharedChatFilterWarningShown;
    private int _reconnectScheduled;
    private bool _windowPositionReady;
    private bool _isAutoScrollPaused;
    private int _newMessagesWhilePaused;
    private bool _cursorOverResizeEdge;
    private readonly DispatcherTimer _windowPositionSaveTimer = new() { Interval = TimeSpan.FromMilliseconds(400) };
    private readonly DispatcherTimer _settingsSaveTimer = new() { Interval = TimeSpan.FromMilliseconds(350) };
    private readonly DispatcherTimer _channelUpdateTimer = new() { Interval = TimeSpan.FromMilliseconds(450) };
    private readonly DispatcherTimer _chatOnlyHitTestTimer = new() { Interval = TimeSpan.FromMilliseconds(40) };
    private readonly DispatcherTimer _chatOnlyGearHoldTimer = new() { Interval = TimeSpan.FromMilliseconds(5) };
    private readonly DispatcherTimer _suppressGearClickTimer = new() { Interval = TimeSpan.FromMilliseconds(300) };
    private bool _cursorOverChatOnlyGear;
    private bool _chatOnlyGearDragActive;
    private bool _suppressNextGearClick;
    private TextBox? _activeColorInput;
    private SettingsWindow _settingsWindow = null!;
    private bool _chatOnlyGearPopupOffsetNudge;
    private NativeMethods.Point _chatOnlyGearDragStart;
    private double _chatOnlyGearWindowStartLeft;
    private double _chatOnlyGearWindowStartTop;
    private bool _popoutScriptRegistered;
    private bool _popoutNavigationHandlerRegistered;
    private bool _popoutResourceRequestHandlerRegistered;
    private bool _twitchCaptureScriptRegistered;
    private bool _twitchPublicChatActive;
    private WebView2? _twitchPopoutView;
    private bool _channelUpdatePending;
    private int _connectionModeChangeVersion;
    private long _lastMessageNotificationSoundTimestamp;
    private static readonly string TwitchCaptureScript = ScriptLoader.Load("TwitchCaptureScript.js");
    private static readonly string TwitchMessagesOnlyScript = ScriptLoader.Load("TwitchMessagesOnlyScript.js");

    public MainWindow()
    {
        _settings = AppSettings.Load();
        SetImageCacheCapacities(_settings.ConnectionMode);
        _settings.IsTransparent = false;
        DataContext = new OverlayViewModel(_settings);
        InitializeComponent();
        _settingsWindow = new SettingsWindow(this);
        _settingsWindow.AddHandler(
            ComboBox.PreviewMouseLeftButtonDownEvent,
            new MouseButtonEventHandler(ComboBoxSurface_PreviewMouseLeftButtonDown),
            true);
        _settingsWindow.AddHandler(
            TextBox.TextChangedEvent,
            new TextChangedEventHandler(MainChannelTextBox_TextChanged),
            true);
        AddHandler(FrameworkElement.LoadedEvent, new RoutedEventHandler(EnsureButtonAutomationName), true);
        EnsureLoadedButtonAutomationNames(this);
        _settingsWindow.MainChannelComboBox.ItemsSource = ViewModel.RecentChannels;
        _settingsWindow.AppVersionText.Text = $"Versão {GetType().Assembly.GetName().Version?.ToString(3) ?? "desconhecida"}";
        UpdateColorPreview(_settingsWindow.AccentColorInput.Text, _settingsWindow.AccentColorPreviewDot, _settingsWindow.AccentColorPreviewText);
        UpdateSettingsPreview();
        UpdateColorPreview(_settingsWindow.MessageBackgroundColorInput.Text, _settingsWindow.MessageBackgroundColorPreviewDot, _settingsWindow.MessageBackgroundColorPreviewText);
        RestoreWindowPosition();
        _windowPositionSaveTimer.Tick += WindowPositionSaveTimer_Tick;
        _channelUpdateTimer.Tick += ChannelUpdateTimer_Tick;
        _settingsSaveTimer.Tick += (_, _) =>
        {
            _settingsSaveTimer.Stop();
            SaveSettingsNow();
        };
        ChatOnlyGearPopup.CustomPopupPlacementCallback = ChatOnlyGearPopup_Placement;
        LocationChanged += MainWindow_LocationChanged;
        _chatOnlyHitTestTimer.Tick += ChatOnlyHitTestTimer_Tick;
        _chatOnlyGearHoldTimer.Tick += ChatOnlyGearHoldTimer_Tick;
        _suppressGearClickTimer.Tick += (_, _) =>
        {
            _suppressNextGearClick = false;
            _suppressGearClickTimer.Stop();
        };
        _settingsWindow.FilterModeComboBox.SelectedIndex = (int)_settings.FilterMode;
        _settingsWindow.ConnectionModeComboBox.SelectedIndex = (int)_settings.ConnectionMode;
        _settingsWindow.NotificationSoundComboBox.SelectedIndex = (int)_settings.NotificationSound;
        UpdateConnectionModeUi();
        try
        {
            _twitchCredentials = TwitchCredentialStore.LoadCredentials(_settings.TwitchUsername);
            if (!string.IsNullOrWhiteSpace(_twitchCredentials?.Username))
            {
                ViewModel.TwitchUsername = _twitchCredentials.Username;
            }

            if (_twitchCredentials is not null)
            {
                _settingsWindow.TwitchAuthorizationStatus.Text = string.IsNullOrWhiteSpace(_twitchCredentials.Username)
                    ? "Credenciais Twitch salvas neste dispositivo."
                    : $"Credenciais Twitch salvas para @{_twitchCredentials.Username}.";
            }
        }
        catch (Exception ex) when (ex is IOException or FormatException or System.ComponentModel.Win32Exception)
        {
            ViewModel.UpdateDiagnostic($"Não foi possível ler a autorização Twitch armazenada: {ex.Message}");
        }

        if (!string.IsNullOrWhiteSpace(_settings.LoadWarning))
        {
            ViewModel.ConnectionStatus = "Configuração recuperada";
            ViewModel.UpdateDiagnostic(_settings.LoadWarning);
        }

        try
        {
            WindowsStartupRegistration.SetEnabled(_settings.StartWithWindows);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            ViewModel.UpdateDiagnostic($"Não foi possível atualizar a inicialização com o Windows: {ex.Message}");
        }

        UpdateConnectionModeUi();
        this.Closed += MainWindow_Closed;
        this.Loaded += MainWindow_Loaded;
        this.StateChanged += MainWindow_StateChanged;
        Activated += (_, _) => UpdateTwitchWebViewMemoryTarget();
        Deactivated += (_, _) => UpdateTwitchWebViewMemoryTarget();
        _chatClient.StatusChanged += (_, status) =>
        {
            DispatchToUi(() =>
            {
                ViewModel.ConnectionStatus = status;
                if (_settings.ConnectionMode == ChatConnectionMode.AuthenticatedIrc)
                {
                    if (_chatClient.IsConnected)
                    {
                        SetConnected(true);
                    }
                    else if (Volatile.Read(ref _reconnectScheduled) == 0)
                    {
                        SetConnected(false);
                    }
                }
                RefreshChatPanels();
                QueueSettingsSave();
            });
        };

        _chatClient.MessageReceived += (_, message) =>
        {
            if (_settings.FilterMode == ChatFilterMode.JoinedChats)
            {
                _ = ResolveJoinedChatMessageAndAppendAsync(message);
            }
            else if (message.BadgeReferences.Count == 0)
            {
                DispatchToUi(() => AppendMessageToFeed(message));
            }
            else
            {
                _ = ResolveBadgesAndAppendAsync(message);
            }
        };
        _chatClient.MessageDeleted += (_, messageId) => DispatchToUi(() =>
        {
            _chatHistory.RemoveAll(message => string.Equals(message.MessageId, messageId, StringComparison.Ordinal));
            var deletedRow = ViewModel.Messages.FirstOrDefault(row => row.SourceMessage.MessageId == messageId);
            if (deletedRow is not null)
            {
                ViewModel.Messages.Remove(deletedRow);
                ScrollChatToLatestMessage();
            }
        });
        _chatClient.UserMessagesCleared += (_, clear) => DispatchToUi(() =>
        {
            _chatHistory.RemoveAll(message =>
                string.Equals(message.Channel, clear.Channel, StringComparison.OrdinalIgnoreCase) &&
                (clear.Username is null ||
                 string.Equals(message.LoginName, clear.Username, StringComparison.OrdinalIgnoreCase)));

            UpdateSettingsPreview();
            RefreshMessageFeed();
        });

        _chatClient.ErrorReported += (_, error) =>
        {
            DispatchToUi(() =>
            {
                ViewModel.UpdateDiagnostic($"Erro: {error}");
                if (error.Contains("autenticação", StringComparison.OrdinalIgnoreCase))
                {
                    SetConnected(false);
                    ViewModel.ConnectionStatus = "Autenticação necessária";
                }
                else if (_settings.AutoReconnect && _isConnected && Interlocked.CompareExchange(ref _reconnectScheduled, 1, 0) == 0)
                {
                    _ = ReconnectAsync();
                }
            });
        };
        _chatClient.ParseErrorReported += (_, diagnostic) =>
            DispatchToUi(() => ViewModel.UpdateDiagnostic(diagnostic));
        _chatClient.ReconnectRequested += (_, _) => DispatchToUi(() =>
        {
            ViewModel.UpdateDiagnostic("A Twitch solicitou uma reconexão.");
            if (_settings.AutoReconnect &&
                Interlocked.CompareExchange(ref _reconnectScheduled, 1, 0) == 0)
            {
                _ircReconnectPending = true;
                _ = ReconnectAsync();
            }
        });

        if (_settings.StartMinimized && !_settings.StartTransparentAndConnected)
        {
            WindowState = WindowState.Minimized;
        }

        var viewModel = ViewModel;
        var defaultFilterMode = (int)_settings.FilterMode;
        if (_settingsWindow.FilterModeComboBox.SelectedIndex != defaultFilterMode)
        {
            _settingsWindow.FilterModeComboBox.SelectedIndex = defaultFilterMode;
        }
    }

    public OverlayViewModel ViewModel => (OverlayViewModel)DataContext;

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        UpdateSettingsPreview();
        ApplyAppearance();
        if (string.IsNullOrWhiteSpace(ViewModel.MainChannel))
        {
            WindowState = WindowState.Normal;
            ViewModel.ConnectionStatus = "Selecione uma live nas configurações";
            ShowPreferencesPage(_settingsWindow.ChatPreferencesPage, _settingsWindow.ChatPreferencesButton);
            ToggleSettingsPopup(true);
            return;
        }

        if (_settings.StartMinimized && !_settings.StartTransparentAndConnected)
        {
            return;
        }

        if (_settings.StartTransparentAndConnected)
        {
            SetChatOnlyMode(true);
        }

        await ConnectAsync(showError: false, allowAuthorization: false);
    }

    private void MainWindow_Closed(object? sender, EventArgs e)
    {
        _settingsWindow.AllowClose = true;
        _settingsWindow.Close();
        _windowPositionSaveTimer.Stop();
        _settingsSaveTimer.Stop();
        SaveWindowPosition();
        SaveSettingsNow();
        _lifetimeCts.Cancel();
        _authorizationCts?.Cancel();
        _chatOnlyHitTestTimer.Stop();
        _chatOnlyGearHoldTimer.Stop();
        _suppressGearClickTimer.Stop();
        DisposeTwitchPopoutView();
        _chatClient.Dispose();
        UnregisterHotKey();
    }

    private async void ConnectButton_Click(object sender, RoutedEventArgs e)
    {
        if (_twitchConnectionActive)
        {
            DisconnectTwitch();
            return;
        }

        await ConnectAsync();
    }

    private void ToggleSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        ToggleSettingsPopup(!_settingsWindow.IsVisible);
    }

    private void CloseSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        ToggleSettingsPopup(false);
    }

    private void ToggleSettingsPopup(bool isOpen)
    {
        ViewModel.SetSettingsVisible(isOpen);
        if (isOpen)
        {
            if (_settingsWindow.Owner is null)
            {
                _settingsWindow.Owner = this;
            }

            _settingsWindow.Show();
            _settingsWindow.Activate();
        }
        else if (_settingsWindow.IsVisible)
        {
            _settingsWindow.Hide();
        }

        QueueSettingsSave();
    }

    internal void SettingsWindowHidden()
    {
        ViewModel.SetSettingsVisible(false);
        QueueSettingsSave();
    }

    internal void InvokeSettingsHandler(string handlerName, object sender, RoutedEventArgs e)
    {
        var handler = GetType().GetMethod(
            handlerName,
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingMethodException(GetType().FullName, handlerName);

        try
        {
            handler.Invoke(this, [sender, e]);
        }
        catch (TargetInvocationException exception) when (exception.InnerException is { } innerException)
        {
            ExceptionDispatchInfo.Capture(innerException).Throw();
        }
    }

    private void MainChannelTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!IsInitialized ||
            e.OriginalSource is not TextBox { TemplatedParent: ComboBox comboBox } ||
            !ReferenceEquals(comboBox, _settingsWindow.MainChannelComboBox))
        {
            return;
        }

        RefreshMessageFeed();
        QueueSettingsSave();
        ScheduleChannelUpdate();
    }

    private void ChatPreferencesButton_Click(object sender, RoutedEventArgs e) => ShowPreferencesPage(_settingsWindow.ChatPreferencesPage, _settingsWindow.ChatPreferencesButton);

    private void ConnectionsPreferencesButton_Click(object sender, RoutedEventArgs e) => ShowPreferencesPage(_settingsWindow.ConnectionsPreferencesPage, _settingsWindow.ConnectionsPreferencesButton);

    private void GeneralPreferencesButton_Click(object sender, RoutedEventArgs e) => ShowPreferencesPage(_settingsWindow.GeneralPreferencesPage, _settingsWindow.GeneralPreferencesButton);

    private void WidgetsPreferencesButton_Click(object sender, RoutedEventArgs e) => ShowPreferencesPage(_settingsWindow.WidgetsPreferencesPage, _settingsWindow.WidgetsPreferencesButton);

    private void ShowPreferencesPage(FrameworkElement page, Button activeButton)
    {
        _settingsWindow.ChatPreferencesPage.Visibility = Visibility.Collapsed;
        _settingsWindow.ConnectionsPreferencesPage.Visibility = Visibility.Collapsed;
        _settingsWindow.GeneralPreferencesPage.Visibility = Visibility.Collapsed;
        _settingsWindow.WidgetsPreferencesPage.Visibility = Visibility.Collapsed;
        page.Visibility = Visibility.Visible;

        foreach (var button in new[]
                 {
                     _settingsWindow.ChatPreferencesButton,
                     _settingsWindow.ConnectionsPreferencesButton,
                     _settingsWindow.GeneralPreferencesButton,
                     _settingsWindow.WidgetsPreferencesButton
                 })
        {
            button.Tag = null;
        }

        activeButton.Tag = "Selected";
        if (SystemParameters.ClientAreaAnimation)
        {
            page.Opacity = 0;
            page.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(160))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            });
        }
        else
        {
            page.BeginAnimation(OpacityProperty, null);
            page.Opacity = 1;
        }
        if (page is ScrollViewer scrollViewer)
        {
            scrollViewer.ScrollToTop();
        }
    }

    private void RestoreSectionDefaults_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string section })
        {
            return;
        }

        var defaults = new AppSettings();
        switch (section)
        {
            case "Appearance":
                _settings.ClickThroughEnabled = defaults.ClickThroughEnabled;
                _settings.ShowUserNames = defaults.ShowUserNames;
                _settings.UserNameAboveMessage = defaults.UserNameAboveMessage;
                _settings.UseTwitchNameColors = defaults.UseTwitchNameColors;
                _settings.ShowTimestamps = defaults.ShowTimestamps;
                _settings.ShowBadges = defaults.ShowBadges;
                _settings.ShowModerators = defaults.ShowModerators;
                _settings.ShowVips = defaults.ShowVips;
                _settings.EnableSound = defaults.EnableSound;
                _settings.NotifyNewMessages = defaults.NotifyNewMessages;
                _settings.NotificationSound = defaults.NotificationSound;
                _settings.NotificationVolumePercent = defaults.NotificationVolumePercent;
                _settings.BackgroundOpacity = defaults.BackgroundOpacity;
                _settings.TextOpacity = defaults.TextOpacity;
                _settings.ShowChatTextOutline = defaults.ShowChatTextOutline;
                _settings.UseMessageBackgroundOpacity = defaults.UseMessageBackgroundOpacity;
                _settings.MessageBackgroundOpacity = defaults.MessageBackgroundOpacity;
                _settings.FontSize = defaults.FontSize;
                _settings.MessageSpacing = defaults.MessageSpacing;
                _settings.MessageBackgroundColor = defaults.MessageBackgroundColor;
                ViewModel.RefreshSettingsBindings(
                    nameof(ViewModel.ClickThroughEnabled),
                    nameof(ViewModel.ShowUserNames),
                    nameof(ViewModel.UserNameAboveMessage),
                    nameof(ViewModel.UseTwitchNameColors),
                    nameof(ViewModel.ShowTimestamps),
                    nameof(ViewModel.ShowBadges),
                    nameof(ViewModel.ShowModerators),
                    nameof(ViewModel.ShowVips),
                    nameof(ViewModel.EnableSound),
                    nameof(ViewModel.NotifyNewMessages),
                    nameof(ViewModel.NotificationVolumePercent),
                    nameof(ViewModel.ShowChatTextOutline),
                    nameof(ViewModel.UseMessageBackgroundOpacity),
                    nameof(ViewModel.MessageBackgroundOpacity),
                    nameof(ViewModel.FontSize),
                    nameof(ViewModel.TextOpacity),
                    nameof(ViewModel.MessageSpacing),
                    nameof(ViewModel.MessageBackgroundColor));
                _settingsWindow.NotificationSoundComboBox.SelectedIndex = (int)defaults.NotificationSound;
                UpdateColorPreview(_settings.HighlightColor, _settingsWindow.AccentColorPreviewDot, _settingsWindow.AccentColorPreviewText);
                UpdateColorPreview(_settings.MessageBackgroundColor, _settingsWindow.MessageBackgroundColorPreviewDot, _settingsWindow.MessageBackgroundColorPreviewText);
                UpdateSettingsPreview();
                ApplyAppearance();
                RefreshMessageFeed();
                break;

            case "Chat":
                _settings.FilterMode = defaults.FilterMode;
                _settings.HideCommands = defaults.HideCommands;
                _settings.DimOtherChannelMessages = defaults.DimOtherChannelMessages;
                _settings.HighlightedUsers = defaults.HighlightedUsers;
                _settings.HighlightColor = defaults.HighlightColor;
                ReplaceItems(ViewModel.SelectedChannels, defaults.SelectedChannels);
                ReplaceItems(ViewModel.BlockedChannels, defaults.BlockedChannels);
                ReplaceItems(ViewModel.BlockedUsers, defaults.BlockedUsers);
                ReplaceItems(ViewModel.BlockedTerms, SplitTerms(defaults.BlockedTerms));
                ReplaceItems(ViewModel.HighlightTerms, SplitTerms(defaults.HighlightTerms));
                ViewModel.RefreshSettingsBindings(
                    nameof(ViewModel.FilterMode),
                    nameof(ViewModel.HideCommands),
                    nameof(ViewModel.DimOtherChannelMessages),
                    nameof(ViewModel.HighlightColor));
                _settingsWindow.FilterModeComboBox.SelectedIndex = (int)defaults.FilterMode;
                _settingsWindow.TestFilterResult.Text = string.Empty;
                RefreshMessageFeed();
                if (_twitchConnectionActive &&
                    _settings.ConnectionMode == ChatConnectionMode.AuthenticatedIrc)
                {
                    ScheduleChannelUpdate();
                }
                break;

            case "Connection":
                _settingsWindow.ConnectionModeComboBox.SelectedIndex = (int)defaults.ConnectionMode;
                break;

            case "General":
                var previousStartWithWindows = _settings.StartWithWindows;
                _settings.AutoReconnect = defaults.AutoReconnect;
                _settings.StartMinimized = defaults.StartMinimized;
                _settings.StartTransparentAndConnected = defaults.StartTransparentAndConnected;
                _settings.StartWithWindows = defaults.StartWithWindows;
                _settings.HideSettingsDuringUse = defaults.HideSettingsDuringUse;
                _settings.MaxVisibleMessages = defaults.MaxVisibleMessages;
                ViewModel.RefreshSettingsBindings(
                    nameof(ViewModel.AutoReconnect),
                    nameof(ViewModel.StartMinimized),
                    nameof(ViewModel.StartTransparentAndConnected),
                    nameof(ViewModel.StartWithWindows),
                    nameof(ViewModel.HideSettingsDuringUse),
                    nameof(ViewModel.MaxVisibleMessages));
                try
                {
                    WindowsStartupRegistration.SetEnabled(defaults.StartWithWindows);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
                {
                    _settings.StartWithWindows = previousStartWithWindows;
                    ViewModel.RefreshSettingsBindings(nameof(ViewModel.StartWithWindows));
                    ViewModel.UpdateDiagnostic($"Não foi possível restaurar a inicialização com o Windows: {exception.Message}");
                }
                break;
        }

        QueueSettingsSave();
    }

    private static void ReplaceItems<T>(ObservableCollection<T> target, IEnumerable<T> values)
    {
        target.Clear();
        foreach (var value in values)
        {
            target.Add(value);
        }
    }

    private static IEnumerable<string> SplitTerms(string terms) =>
        terms.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private void SavePreferences_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsInitialized)
        {
            return;
        }

        if (sender is CheckBox checkBox)
        {
            checkBox.GetBindingExpression(ToggleButton.IsCheckedProperty)?.UpdateSource();
            if (checkBox.Name == "StartWithWindowsCheckBox")
            {
                try
                {
                    WindowsStartupRegistration.SetEnabled(checkBox.IsChecked == true);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
                {
                    ViewModel.StartWithWindows = checkBox.IsChecked != true;
                    checkBox.IsChecked = ViewModel.StartWithWindows;
                    ViewModel.UpdateDiagnostic($"Não foi possível alterar a inicialização com o Windows: {ex.Message}");
                    return;
                }
            }
        }

        UpdateSettingsPreview();
        ApplyAppearance();
        RefreshMessageFeed();
        if (_isConnected && _settings.HideSettingsDuringUse)
        {
            HideSettingsDuringUse();
        }

        QueueSettingsSave();
    }

    private void NotificationSoundComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsInitialized ||
            _settingsWindow.NotificationSoundComboBox.SelectedIndex < 0 ||
            !Enum.IsDefined((MessageNotificationSound)_settingsWindow.NotificationSoundComboBox.SelectedIndex))
        {
            return;
        }

        var selectedSound = (MessageNotificationSound)_settingsWindow.NotificationSoundComboBox.SelectedIndex;
        if (_settings.NotificationSound == selectedSound)
        {
            return;
        }

        _settings.NotificationSound = selectedSound;
        QueueSettingsSave();
        _ = PlayNotificationSoundAsync(selectedSound, _settings.NotificationVolumePercent);
    }

    private void ComboBoxSurface_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (FindVisualAncestor<ComboBox>(e.OriginalSource as DependencyObject) is not { } comboBox ||
            e.GetPosition(comboBox).X >= comboBox.ActualWidth - 28)
        {
            return;
        }

        comboBox.Focus();
        comboBox.IsDropDownOpen = true;
        e.Handled = true;
    }

    private static T? FindVisualAncestor<T>(DependencyObject? element) where T : DependencyObject
    {
        while (element is not null)
        {
            if (element is T ancestor)
            {
                return ancestor;
            }

            element = VisualTreeHelper.GetParent(element);
        }

        return null;
    }

    private void NotificationSoundPreviewButton_Click(object sender, RoutedEventArgs e)
    {
        if (_settingsWindow.NotificationSoundComboBox.SelectedIndex >= 0 &&
            Enum.IsDefined((MessageNotificationSound)_settingsWindow.NotificationSoundComboBox.SelectedIndex))
        {
            _ = PlayNotificationSoundAsync(
                (MessageNotificationSound)_settingsWindow.NotificationSoundComboBox.SelectedIndex,
                _settings.NotificationVolumePercent);
        }
    }

    private void NotificationVolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (IsInitialized)
        {
            QueueSettingsSave();
        }
    }

    private void FontSizeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (IsInitialized)
        {
            UpdateSettingsPreview();
            Dispatcher.BeginInvoke(new Action(() =>
            {
                RefreshMessageFeed();
                QueueSettingsSave();
            }), DispatcherPriority.DataBind);
        }
    }

    private void SaveTextPreference_Changed(object sender, TextChangedEventArgs e)
    {
        if (IsInitialized)
        {
            RefreshMessageFeed();
            QueueSettingsSave();
            if (ReferenceEquals(sender, _settingsWindow.SelectedChannelsTextBox))
            {
                ScheduleChannelUpdate();
            }
        }
    }

    private void RecentChannelsComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_settingsWindow.MainChannelComboBox.SelectedItem is string channel)
        {
            ViewModel.MainChannel = channel;
            ScheduleChannelUpdate();
            QueueSettingsSave();
        }
    }

    private void ScheduleChannelUpdate()
    {
        _channelUpdatePending = true;
        _channelUpdateTimer.Stop();
        _channelUpdateTimer.Start();
    }

    private async void ChannelUpdateTimer_Tick(object? sender, EventArgs e)
    {
        _channelUpdateTimer.Stop();
        if (Volatile.Read(ref _connectInProgress) != 0)
        {
            _channelUpdatePending = true;
            return;
        }

        _channelUpdatePending = false;
        if (!_twitchConnectionActive)
        {
            return;
        }

        try
        {
            if (_settings.ConnectionMode == ChatConnectionMode.PublicPopout)
            {
                var channel = NormalizeChannel(ViewModel.MainChannel);
                if (string.IsNullOrWhiteSpace(channel))
                {
                    return;
                }

                await ShowTwitchPopoutAsync(channel);
                RefreshChatPanels();
                UpdateChatOnlyControlsVisibility();
                ViewModel.ConnectionStatus = "Conectado";
                RememberRecentChannel(channel);
            }
            else
            {
                await _chatClient.UpdateChannelsAsync(GetChannelsToJoin(_settings), _lifetimeCts.Token);
                RefreshMessageFeed();
            }

            QueueSettingsSave();
        }
        catch (OperationCanceledException) when (_lifetimeCts.IsCancellationRequested)
        {
        }
        catch (Exception ex) when (
            ex is IOException or InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            ViewModel.UpdateDiagnostic($"Não foi possível atualizar os canais automaticamente: {ex.Message}");
        }
    }

    private void RememberRecentChannel(string channel)
    {
        var normalized = NormalizeChannel(channel);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return;
        }

        var previous = ViewModel.RecentChannels.FirstOrDefault(
            item => string.Equals(item, normalized, StringComparison.OrdinalIgnoreCase));
        if (previous is not null)
        {
            ViewModel.RecentChannels.Remove(previous);
        }

        ViewModel.RecentChannels.Insert(0, normalized);
        while (ViewModel.RecentChannels.Count > 10)
        {
            ViewModel.RecentChannels.RemoveAt(ViewModel.RecentChannels.Count - 1);
        }

        _settings.RecentChannels = ViewModel.RecentChannels.ToArray();
    }

    private void QueueSettingsSave()
    {
        if (!IsInitialized || Dispatcher.HasShutdownStarted)
        {
            return;
        }

        _settingsSaveTimer.Stop();
        _settingsSaveTimer.Start();
    }

    public void QueueSettingsSaveForExternalChanges() => QueueSettingsSave();

    private void SaveSettingsNow()
    {
        try
        {
            _settings.Save();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ViewModel.UpdateDiagnostic($"Não foi possível salvar as configurações: {ex.Message}");
        }
    }

    private void AppearanceSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!IsInitialized)
        {
            return;
        }

        RefreshMessageFeed();
        QueueSettingsSave();
    }

    private void MaxVisibleMessagesSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (IsInitialized)
        {
            QueueSettingsSave();
        }
    }

    private void HighlightColor_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!IsInitialized)
        {
            return;
        }

        var isValid = _settingsWindow.AccentColorInput.Text.Trim() is { Length: 7 } colorText &&
                      colorText.StartsWith('#') &&
                      ParseHexColor(colorText) is not null;
        _settingsWindow.AccentColorStatus.Text = isValid ? string.Empty : "Informe uma cor hexadecimal com 6 dígitos.";
        _settingsWindow.AccentColorStatus.Visibility = isValid ? Visibility.Collapsed : Visibility.Visible;
        if (isValid)
        {
            var selectedColor = _settingsWindow.AccentColorInput.Text.Trim();
            ViewModel.HighlightColor = selectedColor;
            UpdateColorPreview(selectedColor, _settingsWindow.AccentColorPreviewDot, _settingsWindow.AccentColorPreviewText);
            RefreshMessageFeed();
            QueueSettingsSave();
        }
    }

    private void MessageBackgroundColor_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!IsInitialized)
        {
            return;
        }

        var colorText = _settingsWindow.MessageBackgroundColorInput.Text.Trim();
        var isValid = colorText.Length == 7 &&
                      colorText.StartsWith('#') &&
                      ParseHexColor(colorText) is not null;
        _settingsWindow.MessageBackgroundColorStatus.Text = isValid ? string.Empty : "Informe uma cor hexadecimal com 6 dígitos.";
        _settingsWindow.MessageBackgroundColorStatus.Visibility = isValid ? Visibility.Collapsed : Visibility.Visible;
        if (isValid)
        {
            ViewModel.MessageBackgroundColor = colorText;
            UpdateColorPreview(colorText, _settingsWindow.MessageBackgroundColorPreviewDot, _settingsWindow.MessageBackgroundColorPreviewText);
            UpdateSettingsPreview();
            RefreshMessageFeed();
            QueueSettingsSave();
        }
    }

    private static void UpdateColorPreview(string colorText, Border swatch, TextBlock description)
    {
        if (ParseHexColor(colorText) is not { } color)
        {
            return;
        }

        var normalizedHex = $"#{color.R:X2}{color.G:X2}{color.B:X2}";
        swatch.Background = new SolidColorBrush(color);
        description.Text = $"{GetColorName(color)} {normalizedHex}";
        description.ToolTip = $"Cor selecionada: {GetColorName(color)} {normalizedHex}";
    }

    private void UpdateSettingsPreview()
    {
        if (_settingsWindow?.PreviewMessageText is null)
        {
            return;
        }

        _settingsWindow.PreviewMessageText.FontSize = ViewModel.FontSize;
        _settingsWindow.PreviewMessageText.Opacity = ViewModel.TextOpacity;
        _settingsWindow.PreviewMessageText.Effect = _settings.ShowChatTextOutline
            ? Application.Current.TryFindResource("ChatTextOutline") as System.Windows.Media.Effects.Effect
            : null;
        var color = ParseHexColor(_settings.MessageBackgroundColor) ?? Color.FromRgb(22, 29, 44);
        var opacity = _settings.UseMessageBackgroundOpacity
            ? Math.Clamp(_settings.MessageBackgroundOpacity, 0.1, 1.0)
            : 1.0;
        _settingsWindow.PreviewMessageBorder.Background = new SolidColorBrush(
            Color.FromArgb((byte)Math.Round(255 * opacity), color.R, color.G, color.B));
    }

    private static string GetColorName(Color color)
    {
        (string Name, Color Color)[] namedColors =
        [
            ("Preto", Colors.Black),
            ("Branco", Colors.White),
            ("Cinza", Colors.Gray),
            ("Cinza claro", Colors.Silver),
            ("Vinho", Color.FromRgb(127, 0, 0)),
            ("Vermelho", Colors.Red),
            ("Laranja", Color.FromRgb(255, 127, 0)),
            ("Amarelo", Colors.Yellow),
            ("Oliva", Color.FromRgb(127, 127, 0)),
            ("Verde", Colors.Lime),
            ("Verde escuro", Color.FromRgb(0, 127, 0)),
            ("Ciano", Colors.Cyan),
            ("Azul claro", Color.FromRgb(0, 127, 255)),
            ("Azul", Color.FromRgb(94, 162, 255)),
            ("Azul escuro", Color.FromRgb(0, 0, 127)),
            ("Violeta", Color.FromRgb(127, 0, 255)),
            ("Magenta", Colors.Magenta),
            ("Rosa", Color.FromRgb(255, 0, 127)),
            ("Violeta Twitch", Color.FromRgb(169, 112, 255))
        ];

        var nearest = namedColors
            .MinBy(candidate =>
                Math.Pow(color.R - candidate.Color.R, 2) +
                Math.Pow(color.G - candidate.Color.G, 2) +
                Math.Pow(color.B - candidate.Color.B, 2));
        return nearest.Name;
    }

    private void ChooseHighlightColorButton_Click(object sender, RoutedEventArgs e)
    {
        OpenColorPalette(_settingsWindow.AccentColorInput, (UIElement)sender);
    }

    private void ChooseMessageBackgroundColorButton_Click(object sender, RoutedEventArgs e)
    {
        OpenColorPalette(_settingsWindow.MessageBackgroundColorInput, (UIElement)sender);
    }

    private void OpenColorPalette(TextBox colorInput, UIElement placementTarget)
    {
        _activeColorInput = colorInput;
        _settingsWindow.ColorPalettePopup.PlacementTarget = placementTarget;
        _settingsWindow.ColorPalettePopup.IsOpen = true;
    }

    private void PaletteColorButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || _activeColorInput is null)
        {
            return;
        }

        var color = button.Tag switch
        {
            Color selectedColor => $"#{selectedColor.R:X2}{selectedColor.G:X2}{selectedColor.B:X2}",
            string value => value,
            _ => null
        };
        if (color is null)
        {
            return;
        }

        _activeColorInput.Text = color;
        _settingsWindow.ColorPalettePopup.IsOpen = false;
    }

    private void ShowDiagnosticButton_Click(object sender, RoutedEventArgs e)
    {
        MessageBox.Show(
            this,
            ViewModel.LastDiagnostic,
            "Diagnóstico",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void OpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!IsInitialized)
        {
            return;
        }

        ApplyAppearance();
        RefreshMessageFeed();
        QueueSettingsSave();
    }

    private void DispatchToUi(Action action)
    {
        if (Dispatcher.CheckAccess())
        {
            action();
            return;
        }

        if (!Dispatcher.HasShutdownStarted && !Dispatcher.HasShutdownFinished)
        {
            Dispatcher.BeginInvoke(action);
        }
    }

    private void RootBorder_MouseEnter(object sender, MouseEventArgs e)
    {
        AnimateToolbar(1.0);
    }

    private void RootBorder_MouseLeave(object sender, MouseEventArgs e)
    {
        AnimateToolbar(_settings.IsTransparent ? 0.92 : 0.72);
    }

    private void AnimateToolbar(double opacity)
    {
        if (!SystemParameters.ClientAreaAnimation)
        {
            Toolbar.BeginAnimation(OpacityProperty, null);
            Toolbar.Opacity = opacity;
            return;
        }

        Toolbar.BeginAnimation(OpacityProperty, new DoubleAnimation(opacity, TimeSpan.FromMilliseconds(160))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        });
    }

    private void Toolbar_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || IsWindowControl(e.OriginalSource as DependencyObject))
        {
            return;
        }

        e.Handled = true;
        if (e.ClickCount == 2)
        {
            ToggleMaximizeRestore();
            return;
        }

        DragMove();
    }

    private bool IsWindowControl(DependencyObject? source)
    {
        while (source is not null && source != Toolbar)
        {
            if (source is ButtonBase)
            {
                return true;
            }

            source = source is Visual
                ? VisualTreeHelper.GetParent(source)
                : LogicalTreeHelper.GetParent(source);
        }

        return false;
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void MaximizeRestoreButton_Click(object sender, RoutedEventArgs e)
    {
        ToggleMaximizeRestore();
    }

    private void ToggleMaximizeRestore()
    {
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void MainWindow_StateChanged(object? sender, EventArgs e)
    {
        MaximizeIcon.Data = Geometry.Parse(WindowState == WindowState.Maximized
            ? "M7,3 L21,3 L21,17 L17,17 M17,7 L3,7 L3,21 L17,21 Z"
            : "M4,4 L20,4 L20,20 L4,20 Z");
        MaximizeButton.ToolTip = WindowState == WindowState.Maximized ? "Restaurar" : "Maximizar";
        AutomationProperties.SetName(MaximizeButton, WindowState == WindowState.Maximized ? "Restaurar janela" : "Maximizar janela");
        UpdateTwitchWebViewMemoryTarget();
        ScheduleWindowPositionSave();
    }

    private void MainWindow_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        ConnectionStatusText.Visibility = e.NewSize.Width < 380 ? Visibility.Collapsed : Visibility.Visible;
        ScheduleWindowPositionSave();
    }

    private static void EnsureLoadedButtonAutomationNames(DependencyObject element)
    {
        if (element is ButtonBase button)
        {
            SetButtonAutomationName(button);
        }

        foreach (var child in LogicalTreeHelper.GetChildren(element).OfType<DependencyObject>())
        {
            EnsureLoadedButtonAutomationNames(child);
        }
    }

    private void EnsureButtonAutomationName(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is ButtonBase button)
        {
            SetButtonAutomationName(button);
        }
    }

    private static void SetButtonAutomationName(ButtonBase button)
    {
        var existingName = AutomationProperties.GetName(button);
        if (!string.IsNullOrWhiteSpace(existingName) &&
            !string.Equals(existingName, "Botão", StringComparison.Ordinal))
        {
            return;
        }

        var toolTip = button.ToolTip switch
        {
            string text => text,
            ToolTip tip => GetAutomationText(tip.Content),
            _ => null
        };
        var contentText = GetAutomationText((button as ContentControl)?.Content);
        var name = string.IsNullOrWhiteSpace(toolTip) ? contentText : toolTip;
        AutomationProperties.SetName(button, string.IsNullOrWhiteSpace(name) ? button.Name : name);
        if (string.IsNullOrWhiteSpace(AutomationProperties.GetName(button)))
        {
            AutomationProperties.SetName(button, "Botão");
        }
    }

    private static string? GetAutomationText(object? content)
    {
        if (content is string text)
        {
            return text;
        }

        if (content is TextBlock textBlock)
        {
            return textBlock.Text;
        }

        if (content is AccessText accessText)
        {
            return accessText.Text;
        }

        if (content is not DependencyObject element)
        {
            return null;
        }

        foreach (var child in LogicalTreeHelper.GetChildren(element))
        {
            var childText = GetAutomationText(child);
            if (!string.IsNullOrWhiteSpace(childText))
            {
                return childText;
            }
        }

        return null;
    }

    private void ChatMessagesScrollViewer_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        foreach (var message in ViewModel.Messages)
        {
            message.MaxWidth = Math.Max(
                0,
                e.NewSize.Width - GetMessageHorizontalPadding(ViewModel.Settings));
        }
    }

    private void QuickTransparencyToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (IsInitialized && sender is ToggleButton toggle)
        {
            SetChatOnlyMode(toggle.IsChecked == true);
        }
    }

    private void SetChatOnlyMode(bool enabled)
    {
        _settings.IsTransparent = enabled;
        ViewModel.IsTransparent = enabled;
        QuickTransparencyToggle.IsChecked = enabled;
        foreach (var message in ViewModel.Messages)
        {
            message.SetTransparentAppearance(enabled);
        }

        ApplyAppearance();
        RefreshMessageFeed();
        QueueSettingsSave();
    }

    private void SetConnected(bool connected)
    {
        _twitchConnectionActive = connected;
        ViewModel.IsTwitchConnected = connected;
        UpdateOverallConnectionState();
        UpdateConnectionModeUi();
        if (connected && _settings.HideSettingsDuringUse)
        {
            HideSettingsDuringUse();
        }
    }

    private void HideSettingsDuringUse()
    {
        if (!_settingsWindow.IsVisible)
        {
            return;
        }

        _settingsWindow.Hide();
        ViewModel.SetSettingsVisible(false);
        QueueSettingsSave();
    }

    private void UpdateOverallConnectionState()
    {
        _isConnected = _twitchConnectionActive;
        ViewModel.IsConnected = _isConnected;
    }

    private void DisconnectTwitch()
    {
        _ircReconnectPending = false;
        _chatClient.Disconnect();
        if (_settings.ConnectionMode == ChatConnectionMode.PublicPopout &&
            _twitchPublicChatActive)
        {
            _twitchPopoutView?.CoreWebView2?.Stop();
            _twitchPublicChatActive = false;
        }

        SetConnected(false);
        RefreshChatPanels();
        UpdateChatOnlyControlsVisibility();
        ViewModel.ConnectionStatus = _twitchPublicChatActive ? "Conectado" : "Desconectado";
    }

    private void ExitChatOnlyButton_Click(object sender, RoutedEventArgs e)
    {
        if (_chatOnlyGearDragActive || _suppressNextGearClick)
        {
            _suppressNextGearClick = false;
            _suppressGearClickTimer.Stop();
            return;
        }

        SetChatOnlyMode(false);
    }

    private CustomPopupPlacement[] ChatOnlyGearPopup_Placement(Size popupSize, Size targetSize, Point offset)
    {
        var x = Math.Max(0, (targetSize.Width - popupSize.Width) / 2);
        return
        [
            new CustomPopupPlacement(new Point(x, 8), PopupPrimaryAxis.None)
        ];
    }

    private void MainWindow_LocationChanged(object? sender, EventArgs e)
    {
        ScheduleWindowPositionSave();
        if (!ChatOnlyGearPopup.IsOpen)
        {
            return;
        }

        _chatOnlyGearPopupOffsetNudge = !_chatOnlyGearPopupOffsetNudge;
        ChatOnlyGearPopup.HorizontalOffset = _chatOnlyGearPopupOffsetNudge ? 0.01 : 0;
    }

    private void RestoreWindowPosition()
    {
        var left = _settings.WindowLeft;
        var top = _settings.WindowTop;
        if (_settings.WindowWidth is double savedWidth && double.IsFinite(savedWidth))
        {
            Width = Math.Clamp(savedWidth, MinWidth, Math.Max(MinWidth, SystemParameters.VirtualScreenWidth));
        }

        if (_settings.WindowHeight is double savedHeight && double.IsFinite(savedHeight))
        {
            Height = Math.Clamp(savedHeight, MinHeight, Math.Max(MinHeight, SystemParameters.VirtualScreenHeight));
        }

        if (left is null || top is null || !double.IsFinite(left.Value) || !double.IsFinite(top.Value))
        {
            _windowPositionReady = true;
            return;
        }

        var virtualLeft = SystemParameters.VirtualScreenLeft;
        var virtualTop = SystemParameters.VirtualScreenTop;
        var virtualRight = virtualLeft + SystemParameters.VirtualScreenWidth;
        var virtualBottom = virtualTop + SystemParameters.VirtualScreenHeight;
        var minLeft = virtualLeft - Math.Max(0, Width - 96);
        var maxLeft = Math.Max(minLeft, virtualRight - 96);
        var maxTop = Math.Max(virtualTop, virtualBottom - 48);

        Left = Math.Clamp(left.Value, minLeft, maxLeft);
        Top = Math.Clamp(top.Value, virtualTop, maxTop);
        _windowPositionReady = true;
    }

    private void ScheduleWindowPositionSave()
    {
        if (!_windowPositionReady || !IsLoaded || WindowState == WindowState.Minimized)
        {
            return;
        }

        _windowPositionSaveTimer.Stop();
        _windowPositionSaveTimer.Start();
    }

    private void WindowPositionSaveTimer_Tick(object? sender, EventArgs e)
    {
        _windowPositionSaveTimer.Stop();
        SaveWindowPosition();
    }

    private void SaveWindowPosition()
    {
        if (!_windowPositionReady || !IsLoaded || WindowState == WindowState.Minimized)
        {
            return;
        }

        var bounds = WindowState == WindowState.Normal
            ? new Rect(Left, Top, ActualWidth, ActualHeight)
            : RestoreBounds;
        if (bounds.IsEmpty || !double.IsFinite(bounds.Left) || !double.IsFinite(bounds.Top))
        {
            return;
        }

        _settings.WindowLeft = bounds.Left;
        _settings.WindowTop = bounds.Top;
        _settings.WindowWidth = bounds.Width;
        _settings.WindowHeight = bounds.Height;
        QueueSettingsSave();
    }

    private void FilterModeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var comboBox = (ComboBox)sender;
        if (comboBox.SelectedIndex < 0)
        {
            return;
        }

        ViewModel.FilterMode = (ChatFilterMode)comboBox.SelectedIndex;
        UpdateFilterModeUi();
        _sharedChatFilterWarningShown = false;
        RefreshMessageFeed();
        QueueSettingsSave();
        if (_twitchConnectionActive &&
            _settings.ConnectionMode == ChatConnectionMode.AuthenticatedIrc)
        {
            ScheduleChannelUpdate();
        }
    }

    private void WarnWhenJoinedMessageIsFiltered(ChatMessage message, AppSettings settings)
    {
        if (_sharedChatFilterWarningShown ||
            settings.FilterMode != ChatFilterMode.OnlyMyChannel ||
            string.IsNullOrWhiteSpace(message.SourceRoomId) ||
            string.IsNullOrWhiteSpace(message.RoomId) ||
            string.Equals(message.SourceRoomId, message.RoomId, StringComparison.Ordinal))
        {
            return;
        }

        _sharedChatFilterWarningShown = true;
        ViewModel.UpdateDiagnostic(
            "Uma mensagem de outro canal unido foi ocultada. Selecione Chat Unido para exibir mensagens encaminhadas de outros canais.");
        MessageBox.Show(
            this,
            "Esta mensagem veio de outro canal conectado ao chat compartilhado e foi ocultada pelo filtro Meu Chat.\n\nPara exibir mensagens dos canais unidos junto com o canal principal, selecione Chat Unido nas configurações da Twitch.",
            "Mensagem de chat unido ocultada",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private async void ConnectionModeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsInitialized || _settingsWindow.ConnectionModeComboBox.SelectedIndex < 0)
        {
            return;
        }

        var selectedMode = (ChatConnectionMode)_settingsWindow.ConnectionModeComboBox.SelectedIndex;
        if (_settings.ConnectionMode == selectedMode)
        {
            UpdateConnectionModeUi();
            return;
        }

        var resumeTwitchConnection = _twitchConnectionActive ||
                                     _twitchPublicChatActive ||
                                     _chatClient.IsConnected ||
                                     Volatile.Read(ref _connectInProgress) != 0;
        var changeVersion = Interlocked.Increment(ref _connectionModeChangeVersion);
        ViewModel.ConnectionMode = selectedMode;
        SetImageCacheCapacities(selectedMode);
        _authorizationCts?.Cancel();
        UpdateConnectionModeUi();
        ApplyAppearance();
        RefreshMessageFeed();
        _chatClient.Disconnect();
        SetConnected(false);
        if (selectedMode == ChatConnectionMode.AuthenticatedIrc)
        {
            DisposeTwitchPopoutView();
        }
        RefreshChatPanels();
        UpdateChatOnlyControlsVisibility();
        ViewModel.ConnectionStatus = resumeTwitchConnection
            ? "Alterando método de conexão..."
            : "Método alterado · clique em Conectar";
        QueueSettingsSave();

        if (!resumeTwitchConnection)
        {
            return;
        }

        try
        {
            while (Volatile.Read(ref _connectInProgress) != 0)
            {
                await Task.Delay(50, _lifetimeCts.Token);
                if (changeVersion != Volatile.Read(ref _connectionModeChangeVersion))
                {
                    return;
                }
            }

            if (changeVersion == Volatile.Read(ref _connectionModeChangeVersion))
            {
                await ConnectAsync(allowAuthorization: true);
            }
        }
        catch (OperationCanceledException) when (_lifetimeCts.IsCancellationRequested)
        {
        }
    }

    private void UpdateConnectionModeUi()
    {
        var ircMode = _settings.ConnectionMode == ChatConnectionMode.AuthenticatedIrc;
        var hasTwitchAuthorization = ircMode && _twitchCredentials is not null;
        _settingsWindow.ConnectionModeDescription.Text = ircMode
            ? "IRC autenticado: requer autorização da Twitch. Só neste modo estão disponíveis Chat Unido e canais adicionais, ignorar mensagens por canal ou usuário, ocultar comandos e diminuir a opacidade de outros canais. Filtros de termos e personalização visual funcionam nos dois modos."
            : "Chat público: não requer autorização e mostra apenas o canal selecionado. Filtros de termos e personalização visual continuam disponíveis; Chat Unido, canais adicionais, ignorar mensagens por canal ou usuário, ocultar comandos e opacidade por canal são exclusivos do IRC.";
        _settingsWindow.ChatFilterModeInfo.Visibility = Visibility.Collapsed;
        _settingsWindow.AuthenticatedFilterControls.Visibility = hasTwitchAuthorization
            ? Visibility.Visible
            : Visibility.Collapsed;
        _settingsWindow.FilterAuthenticationPrompt.Visibility = ircMode && !hasTwitchAuthorization
            ? Visibility.Visible
            : Visibility.Collapsed;
        _settingsWindow.TwitchAuthorizationPanel.Visibility = ircMode ? Visibility.Visible : Visibility.Collapsed;
        _settingsWindow.TwitchAuthorizationHelp.Visibility = ircMode ? Visibility.Visible : Visibility.Collapsed;
        UpdateFilterModeUi();
    }

    private void UpdateFilterModeUi()
    {
        var joinedChats = _settings.FilterMode == ChatFilterMode.JoinedChats;
        var hasTwitchAuthorization = _settings.ConnectionMode == ChatConnectionMode.AuthenticatedIrc &&
                                     _twitchCredentials is not null;

        _settingsWindow.OtherChannelsFilterControls.Visibility = joinedChats && hasTwitchAuthorization
            ? Visibility.Visible
            : Visibility.Collapsed;
        _settingsWindow.AdditionalChannelsFilterControls.Visibility = joinedChats
            ? Visibility.Visible
            : Visibility.Collapsed;
        _settingsWindow.BlockedChannelsFilterControls.Visibility = joinedChats
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void OpenFilterAuthentication_Click(object sender, RoutedEventArgs e)
    {
        _settingsWindow.ConnectionModeComboBox.SelectedIndex = (int)ChatConnectionMode.AuthenticatedIrc;
        ToggleSettingsPopup(true);
        ShowPreferencesPage(_settingsWindow.ConnectionsPreferencesPage, _settingsWindow.ConnectionsPreferencesButton);
    }

    private void UpdateChatOnlyControlsVisibility()
    {
        var chatOnly = _settings.IsTransparent;
        var publicChatVisible = _twitchPopoutView?.Visibility == Visibility.Visible;
        ExitChatOnlyButton.Visibility = chatOnly && !publicChatVisible
            ? Visibility.Visible
            : Visibility.Collapsed;
        ChatOnlyGearPopup.IsOpen = chatOnly && publicChatVisible;
    }

    private void RefreshChatPanels()
    {
        ChatMessagesScrollViewer.Visibility = Visibility.Visible;
        if (_twitchPopoutView is not null)
        {
            _twitchPopoutView.Visibility = _twitchPublicChatActive
                ? Visibility.Visible
                : Visibility.Collapsed;
        }
    }

    private void PublicChat_WebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            using var document = JsonDocument.Parse(e.WebMessageAsJson);
            var root = document.RootElement;
            if (!_twitchPublicChatActive || root.ValueKind != JsonValueKind.Object)
            {
                return;
            }

            var kind = root.TryGetProperty("kind", out var kindValue)
                ? kindValue.GetString()
                : null;
            if (string.Equals(kind, "capture-ready", StringComparison.Ordinal))
            {
                ViewModel.UpdateDiagnostic("Chat público conectado; captura de mensagens ativa.");
                return;
            }
            if (string.Equals(kind, "capture-error", StringComparison.Ordinal))
            {
                var error = root.TryGetProperty("message", out var errorValue)
                    ? errorValue.GetString()
                    : null;
                ViewModel.UpdateDiagnostic($"Não foi possível iniciar a captura do chat público: {error ?? "erro desconhecido"}");
                return;
            }

            var displayName = root.TryGetProperty("displayName", out var nameValue)
                ? nameValue.GetString()?.Trim() ?? string.Empty
                : string.Empty;
            var text = root.TryGetProperty("message", out var messageValue)
                ? messageValue.GetString() ?? string.Empty
                : string.Empty;
            if (string.IsNullOrWhiteSpace(displayName) || string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            var channel = root.TryGetProperty("channel", out var channelValue)
                ? channelValue.GetString()?.Trim() ?? string.Empty
                : string.Empty;
            var channelImageUrl = root.TryGetProperty("channelImageUrl", out var channelImageValue)
                ? channelImageValue.GetString()?.Trim() ?? string.Empty
                : string.Empty;
            if (!string.IsNullOrWhiteSpace(channelImageUrl) &&
                (!Uri.TryCreate(channelImageUrl, UriKind.Absolute, out var channelImageUri) ||
                 channelImageUri.Scheme != Uri.UriSchemeHttps ||
                 !(channelImageUri.Host.EndsWith(".jtvnw.net", StringComparison.OrdinalIgnoreCase) ||
                   channelImageUri.Host.EndsWith(".twitch.tv", StringComparison.OrdinalIgnoreCase))))
            {
                channelImageUrl = string.Empty;
            }

            var badges = new List<TwitchChatBadge>();
            if (root.TryGetProperty("badges", out var badgesValue) && badgesValue.ValueKind == JsonValueKind.Array)
            {
                foreach (var badgeValue in badgesValue.EnumerateArray())
                {
                    var setId = badgeValue.TryGetProperty("setId", out var setIdValue)
                        ? setIdValue.GetString()?.Trim() ?? string.Empty
                        : string.Empty;
                    var title = badgeValue.TryGetProperty("title", out var titleValue)
                        ? titleValue.GetString()?.Trim() ?? string.Empty
                        : string.Empty;
                    var imageUrl = badgeValue.TryGetProperty("imageUrl", out var imageValue)
                        ? imageValue.GetString()?.Trim() ?? string.Empty
                        : string.Empty;
                    if (string.IsNullOrWhiteSpace(title) ||
                        !Uri.TryCreate(imageUrl, UriKind.Absolute, out var badgeUri) ||
                        badgeUri.Scheme != Uri.UriSchemeHttps ||
                        !(badgeUri.Host.EndsWith(".jtvnw.net", StringComparison.OrdinalIgnoreCase) ||
                          badgeUri.Host.EndsWith(".twitch.tv", StringComparison.OrdinalIgnoreCase)))
                    {
                        continue;
                    }

                    badges.Add(new TwitchChatBadge(
                        string.IsNullOrWhiteSpace(setId) ? title.ToLowerInvariant() : setId,
                        string.Empty,
                        title,
                        badgeUri.AbsoluteUri));
                }
            }

            var emotes = new List<TwitchEmoteRange>();
            if (root.TryGetProperty("emotes", out var emotesValue) && emotesValue.ValueKind == JsonValueKind.Array)
            {
                var lastEmoteEnd = -1;
                foreach (var emoteValue in emotesValue.EnumerateArray())
                {
                    if (emoteValue.ValueKind != JsonValueKind.Object ||
                        !emoteValue.TryGetProperty("id", out var idValue) ||
                        idValue.ValueKind != JsonValueKind.String ||
                        !emoteValue.TryGetProperty("start", out var startValue) ||
                        !startValue.TryGetInt32(out var start) ||
                        !emoteValue.TryGetProperty("end", out var endValue) ||
                        !endValue.TryGetInt32(out var end))
                    {
                        continue;
                    }

                    var emoteId = idValue.GetString();
                    if (string.IsNullOrWhiteSpace(emoteId) ||
                        !emoteId.All(char.IsAsciiDigit) ||
                        start < 0 ||
                        end < start ||
                        end >= text.Length ||
                        start <= lastEmoteEnd)
                    {
                        continue;
                    }

                    emotes.Add(new TwitchEmoteRange(emoteId, start, end));
                    lastEmoteEnd = end;
                }
            }

            var message = new ChatMessage
            {
                DisplayName = displayName,
                LoginName = displayName,
                Message = text,
                Channel = channel,
                OriginChannel = channel,
                ChannelImageUrl = channelImageUrl,
                Badges = badges,
                Emotes = emotes,
                IsModerator = root.TryGetProperty("isModerator", out var moderatorValue) &&
                              moderatorValue.ValueKind == JsonValueKind.True,
                IsVip = root.TryGetProperty("isVip", out var vipValue) && vipValue.ValueKind == JsonValueKind.True,
                IsSubscriber = root.TryGetProperty("isSubscriber", out var subscriberValue) &&
                               subscriberValue.ValueKind == JsonValueKind.True,
                TimestampUtc = DateTimeOffset.UtcNow
            };

            DispatchToUi(() => AppendMessageToFeed(message));
        }
        catch (JsonException ex)
        {
            ViewModel.UpdateDiagnostic($"Não foi possível ler uma mensagem do chat público: {ex.Message}");
        }
    }

    private async Task ResolveBadgesAndAppendAsync(ChatMessage message)
    {
        await ResolveBadgesAsync(message);
        DispatchToUi(() => AppendMessageToFeed(message));
    }

    private async Task ResolveBadgesAsync(ChatMessage message)
    {
        try
        {
            message.Badges = (await _twitchBadgeClient.ResolveBadgesAsync(
                [message.SourceRoomId, message.RoomId],
                message.BadgeReferences,
                _twitchCredentials?.AccessToken ?? string.Empty,
                error => DispatchToUi(() => ViewModel.UpdateDiagnostic(error)),
                _lifetimeCts.Token)).ToList();
        }
        catch (Exception ex) when (
            ex is HttpRequestException or JsonException or IOException or OperationCanceledException or
                InvalidOperationException or FormatException or ArgumentException)
        {
            if (_lifetimeCts.IsCancellationRequested)
            {
                return;
            }

            message.Badges = message.BadgeReferences
                .Select(reference => new TwitchChatBadge(
                    reference.SetId,
                    reference.Version,
                    reference.SetId,
                    string.Empty))
                .ToList();
            DispatchToUi(() => ReportBadgeErrorOncePerMinute($"Não foi possível carregar badges: {ex.Message}"));
        }
    }

    private async Task ResolveJoinedChatMessageAndAppendAsync(ChatMessage message)
    {
        var channelId = string.IsNullOrWhiteSpace(message.SourceRoomId)
            ? message.RoomId
            : message.SourceRoomId;
        var tasks = new List<Task>();
        if (message.BadgeReferences.Count > 0)
        {
            tasks.Add(ResolveBadgesAsync(message));
        }

        if (!string.IsNullOrWhiteSpace(channelId))
        {
            tasks.Add(ResolveChannelImageAsync(message, channelId));
        }

        await Task.WhenAll(tasks);
        DispatchToUi(() => AppendMessageToFeed(message));
    }

    private async Task ResolveChannelImageAsync(ChatMessage message, string channelId)
    {
        try
        {
            message.ChannelImageUrl = await _twitchChannelClient.GetProfileImageUrlAsync(
                channelId,
                _twitchCredentials?.AccessToken ?? string.Empty,
                _lifetimeCts.Token) ?? string.Empty;
        }
        catch (Exception ex) when (
            ex is HttpRequestException or JsonException or OperationCanceledException)
        {
            if (_lifetimeCts.IsCancellationRequested)
            {
                return;
            }

            DispatchToUi(() => ViewModel.UpdateDiagnostic($"Não foi possível carregar a foto do canal Twitch: {ex.Message}"));
        }
    }

    private void AppendMessageToFeed(ChatMessage message)
    {
        _chatHistory.Add(message);
        while (_chatHistory.Count > ViewModel.Settings.MaxVisibleMessages)
        {
            _chatHistory.RemoveAt(0);
        }

        if (!ShouldDisplayMessage(message, ViewModel.Settings))
        {
            WarnWhenJoinedMessageIsFiltered(message, ViewModel.Settings);
            return;
        }

        if (ViewModel.Messages.Count >= ViewModel.Settings.MaxVisibleMessages)
        {
            ViewModel.Messages.RemoveAt(0);
        }

        ViewModel.Messages.Add(CreateChatRow(message, ViewModel.Settings, isNewMessage: true));
        if (_isAutoScrollPaused)
        {
            _newMessagesWhilePaused++;
            UpdateNewMessagesButton();
        }
        else
        {
            ScrollChatToLatestMessage();
        }
        if (ViewModel.Settings.NotifyNewMessages)
        {
            PlayNewMessageNotificationSound();
        }
        else if (ViewModel.Settings.EnableSound &&
                 ContainsAnyTerm(message.Message, ViewModel.Settings.HighlightTerms) &&
                 TryAcquireMessageNotificationSoundCooldown())
        {
            ScheduleMessageNotificationSound(System.Media.SystemSounds.Asterisk.Play);
        }

    }

    private void ChatMessage_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (_settings.IsTransparent && _settings.ClickThroughEnabled)
        {
            e.Handled = true;
        }
    }

    private void CopyChatMessage_Click(object sender, RoutedEventArgs e)
    {
        if (GetChatRowFromContextMenu(sender) is not { } row)
        {
            return;
        }

        try
        {
            Clipboard.SetText(row.SourceMessage.Message);
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.ExternalException or InvalidOperationException)
        {
            ViewModel.UpdateDiagnostic($"Não foi possível copiar a mensagem: {ex.Message}");
        }
    }

    private void BlockChatUser_Click(object sender, RoutedEventArgs e)
    {
        if (GetChatRowFromContextMenu(sender) is not { } row)
        {
            return;
        }

        var user = GetMessageUserName(row.SourceMessage);
        if (string.IsNullOrWhiteSpace(user))
        {
            return;
        }

        if (!ViewModel.BlockedUsers.Contains(user, StringComparer.OrdinalIgnoreCase))
        {
            ViewModel.BlockedUsers.Add(user);
            ViewModel.BlockedUsersText = string.Join(", ", ViewModel.BlockedUsers);
            RefreshMessageFeed();
            QueueSettingsSave();
        }
    }

    private void HighlightChatUser_Click(object sender, RoutedEventArgs e)
    {
        if (GetChatRowFromContextMenu(sender) is not { } row)
        {
            return;
        }

        var user = GetMessageUserName(row.SourceMessage);
        if (string.IsNullOrWhiteSpace(user) ||
            _settings.HighlightedUsers.Contains(user, StringComparer.OrdinalIgnoreCase))
        {
            return;
        }

        _settings.HighlightedUsers.Add(user);
        RefreshMessageFeed();
        QueueSettingsSave();
    }

    private void OpenChatChannel_Click(object sender, RoutedEventArgs e)
    {
        if (GetChatRowFromContextMenu(sender) is not { } row)
        {
            return;
        }

        var channel = NormalizeChannel(row.SourceMessage.EffectiveOrigin);
        if (string.IsNullOrWhiteSpace(channel))
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo($"https://www.twitch.tv/{Uri.EscapeDataString(channel)}")
            {
                UseShellExecute = true
            });
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            ViewModel.UpdateDiagnostic($"Não foi possível abrir o canal no navegador: {ex.Message}");
        }
    }

    private static ChatRowView? GetChatRowFromContextMenu(object sender) =>
        (sender as FrameworkElement)?.DataContext as ChatRowView;

    private static string GetMessageUserName(ChatMessage message)
    {
        var login = message.LoginName.Trim().TrimStart('@');
        return string.IsNullOrWhiteSpace(login)
            ? message.DisplayName.Trim().TrimStart('@')
            : login;
    }

    private void AddFilterChip_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not string category)
        {
            return;
        }

        var input = GetFilterInput(category);
        var items = GetFilterCollection(category);
        var rawValue = input?.Text.Trim() ?? string.Empty;
        if (input is null || items is null || string.IsNullOrWhiteSpace(rawValue))
        {
            return;
        }

        var value = category switch
        {
            "SelectedChannels" or "BlockedChannels" => NormalizeChannel(rawValue),
            "BlockedUsers" => rawValue.TrimStart('@').ToLowerInvariant(),
            _ => rawValue
        };
        var isName = category is "SelectedChannels" or "BlockedChannels" or "BlockedUsers";
        if ((isName && !IsValidTwitchUserName(value)) ||
            (!isName && value.IndexOfAny([',', ';', '\r', '\n']) >= 0))
        {
            ViewModel.UpdateDiagnostic(isName
                ? "O canal ou usuário deve ter de 1 a 25 letras, números ou sublinhados."
                : "Adicione um termo por vez; vírgulas e ponto e vírgula são separadores.");
            return;
        }

        if (!items.Contains(value, StringComparer.OrdinalIgnoreCase))
        {
            items.Add(value);
            RefreshMessageFeed();
            QueueSettingsSave();
            if (category == "SelectedChannels")
            {
                ScheduleChannelUpdate();
            }
        }

        input.Clear();
    }

    private void RemoveFilterChip_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string category, CommandParameter: string value })
        {
            return;
        }

        var items = GetFilterCollection(category);
        if (items is null || !items.Remove(value))
        {
            return;
        }

        if (category == "BlockedUsers")
        {
            ViewModel.BlockedUsersText = string.Join(", ", ViewModel.BlockedUsers);
        }

        RefreshMessageFeed();
        QueueSettingsSave();
        if (category == "SelectedChannels")
        {
            ScheduleChannelUpdate();
        }
    }

    private TextBox? GetFilterInput(string category) => category switch
    {
        "SelectedChannels" => _settingsWindow.SelectedChannelsTextBox,
        "BlockedChannels" => _settingsWindow.BlockedChannelsTextBox,
        "BlockedUsers" => _settingsWindow.BlockedUsersTextBox,
        "BlockedTerms" => _settingsWindow.BlockedTermsTextBox,
        "HighlightTerms" => _settingsWindow.HighlightTermsTextBox,
        _ => null
    };

    private ObservableCollection<string>? GetFilterCollection(string category) => category switch
    {
        "SelectedChannels" => ViewModel.SelectedChannels,
        "BlockedChannels" => ViewModel.BlockedChannels,
        "BlockedUsers" => ViewModel.BlockedUsers,
        "BlockedTerms" => ViewModel.BlockedTerms,
        "HighlightTerms" => ViewModel.HighlightTerms,
        _ => null
    };

    private static bool IsValidTwitchUserName(string value) =>
        value.Length is >= 1 and <= 25 &&
        value.All(character =>
            character is >= 'a' and <= 'z' or
                >= 'A' and <= 'Z' or
                >= '0' and <= '9' or '_');

    private void TestFilter_Click(object sender, RoutedEventArgs e)
    {
        var phrase = _settingsWindow.TestFilterInput.Text.Trim();
        if (string.IsNullOrWhiteSpace(phrase))
        {
            _settingsWindow.TestFilterResult.Text = "Digite uma frase para testar.";
            return;
        }

        var channel = NormalizeChannel(_settings.MainChannel);
        if (string.IsNullOrWhiteSpace(channel))
        {
            _settingsWindow.TestFilterResult.Text = "Defina o canal principal para testar os filtros.";
            return;
        }

        var sample = new ChatMessage
        {
            LoginName = "teste",
            DisplayName = "Teste",
            Message = phrase,
            Channel = channel,
            OriginChannel = channel
        };
        if (!ShouldDisplayMessage(sample, _settings))
        {
            _settingsWindow.TestFilterResult.Text = "Ocultada pelos filtros atuais.";
        }
        else if (ContainsAnyTerm(phrase, _settings.HighlightTerms) ||
                 _settings.HighlightedUsers.Contains("teste", StringComparer.OrdinalIgnoreCase))
        {
            _settingsWindow.TestFilterResult.Text = "Visível e destacada pelos filtros atuais.";
        }
        else
        {
            _settingsWindow.TestFilterResult.Text = "Visível, sem destaque pelos filtros atuais.";
        }
    }

    private void PlayNewMessageNotificationSound()
    {
        if (!ViewModel.Settings.NotifyNewMessages)
        {
            return;
        }

        if (!TryAcquireMessageNotificationSoundCooldown())
        {
            return;
        }

        var sound = ViewModel.Settings.NotificationSound;
        var volumePercent = ViewModel.Settings.NotificationVolumePercent;
        ScheduleMessageNotificationSound(() => _ = PlayNotificationSoundAsync(sound, volumePercent));
    }

    private void ScheduleMessageNotificationSound(Action play)
    {
        Dispatcher.BeginInvoke(
            DispatcherPriority.ContextIdle,
            new Action(() =>
            {
                var timer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher)
                {
                    Interval = MessageNotificationRenderDelay
                };
                void OnTick(object? sender, EventArgs e)
                {
                    timer.Stop();
                    timer.Tick -= OnTick;
                    if (!Dispatcher.HasShutdownStarted && !Dispatcher.HasShutdownFinished)
                    {
                        play();
                    }
                }

                timer.Tick += OnTick;
                timer.Start();
            }));
    }

    private bool TryAcquireMessageNotificationSoundCooldown()
    {
        var now = Stopwatch.GetTimestamp();
        var previous = Volatile.Read(ref _lastMessageNotificationSoundTimestamp);
        if (previous != 0 &&
            Stopwatch.GetElapsedTime(previous, now) <
            TimeSpan.FromMilliseconds(MessageNotificationSoundCooldownMilliseconds))
        {
            return false;
        }

        Interlocked.Exchange(ref _lastMessageNotificationSoundTimestamp, now);
        return true;
    }

    private async Task PlayNotificationSoundAsync(MessageNotificationSound sound, int volumePercent)
    {
        try
        {
            var waveData = CreateNotificationWave(sound, volumePercent);
            await Task.Run(() =>
            {
                using var stream = new MemoryStream(waveData);
                using var player = new System.Media.SoundPlayer(stream);
                player.PlaySync();
            });
        }
        catch (Exception ex) when (
            ex is IOException or InvalidOperationException or System.ComponentModel.Win32Exception or ExternalException)
        {
            DispatchToUi(() => ViewModel.UpdateDiagnostic($"Não foi possível reproduzir o som de notificação: {ex.Message}"));
        }
    }

    private static byte[] CreateNotificationWave(MessageNotificationSound sound, int volumePercent)
    {
        const int sampleRate = 44100;
        const short bitsPerSample = 16;
        const short channels = 1;
        var durationSeconds = sound switch
        {
            MessageNotificationSound.Soft => 0.14,
            MessageNotificationSound.Bell => 0.22,
            _ => 0.10
        };
        var sampleCount = (int)(sampleRate * durationSeconds);
        var dataLength = sampleCount * sizeof(short);
        var volume = Math.Clamp(volumePercent, 0, 100) / 100.0;
        using var stream = new MemoryStream(44 + dataLength);
        using var writer = new BinaryWriter(stream);
        writer.Write("RIFF"u8);
        writer.Write(36 + dataLength);
        writer.Write("WAVEfmt "u8);
        writer.Write(16);
        writer.Write((short)1);
        writer.Write(channels);
        writer.Write(sampleRate);
        writer.Write(sampleRate * channels * bitsPerSample / 8);
        writer.Write((short)(channels * bitsPerSample / 8));
        writer.Write(bitsPerSample);
        writer.Write("data"u8);
        writer.Write(dataLength);

        for (var index = 0; index < sampleCount; index++)
        {
            var progress = (double)index / sampleCount;
            var envelope = sound == MessageNotificationSound.Bell
                ? Math.Exp(-4.5 * progress)
                : Math.Min(1, Math.Min(progress / 0.08, (1 - progress) / 0.08));
            var sample = sound switch
            {
                MessageNotificationSound.Soft => Math.Sin(2 * Math.PI * 660 * index / sampleRate),
                MessageNotificationSound.Bell =>
                    (Math.Sin(2 * Math.PI * 880 * index / sampleRate) +
                     Math.Sin(2 * Math.PI * 1320 * index / sampleRate)) / 2,
                _ => Math.Sin(2 * Math.PI * 880 * index / sampleRate)
            };
            writer.Write((short)(sample * envelope * volume * short.MaxValue * 0.22));
        }

        return stream.ToArray();
    }

    private void RefreshMessageFeed()
    {
        if (!IsInitialized)
        {
            return;
        }

        var visibleMessages = _chatHistory
            .Where(message => ShouldDisplayMessage(message, ViewModel.Settings))
            .TakeLast(ViewModel.Settings.MaxVisibleMessages)
            .Select(message => CreateChatRow(message, ViewModel.Settings))
            .ToArray();
        ViewModel.Messages.Clear();
        foreach (var row in visibleMessages)
        {
            ViewModel.Messages.Add(row);
        }

        if (!_isAutoScrollPaused)
        {
            ScrollChatToLatestMessage();
        }
    }

    private void ScrollChatToLatestMessage()
    {
        if (_isAutoScrollPaused)
        {
            return;
        }

        Dispatcher.BeginInvoke(
            DispatcherPriority.ContextIdle,
            new Action(() =>
            {
                ChatMessagesScrollViewer.UpdateLayout();
                ChatMessagesScrollViewer.ScrollToEnd();
            }));
    }

    private void ChatMessagesScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        var isAtEnd = e.VerticalOffset >= e.ExtentHeight - e.ViewportHeight - 1;
        if (isAtEnd && e.VerticalChange > 0)
        {
            _isAutoScrollPaused = false;
            _newMessagesWhilePaused = 0;
            UpdateNewMessagesButton();
        }
        else if (e.VerticalChange < 0 && e.ExtentHeightChange == 0 && e.ViewportHeightChange == 0)
        {
            _isAutoScrollPaused = true;
        }
    }

    private void NewMessagesButton_Click(object sender, RoutedEventArgs e)
    {
        _isAutoScrollPaused = false;
        _newMessagesWhilePaused = 0;
        UpdateNewMessagesButton();
        ScrollChatToLatestMessage();
    }

    private void UpdateNewMessagesButton()
    {
        NewMessagesCountText.Text = _newMessagesWhilePaused == 1
            ? "1 nova mensagem"
            : $"{_newMessagesWhilePaused} novas mensagens";
        AutomationProperties.SetName(
            NewMessagesButton,
            $"Rolar para {_newMessagesWhilePaused} novas mensagens");
        NewMessagesButton.Visibility = _isAutoScrollPaused && _newMessagesWhilePaused > 0
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private async Task ShowTwitchPopoutAsync(string channel)
    {
        var popoutView = GetOrCreateTwitchPopoutView();
        try
        {
            await popoutView.EnsureCoreWebView2Async();
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            DisposeTwitchPopoutView();
            throw new InvalidOperationException(
                "Não foi possível iniciar o navegador interno WebView2. Instale ou atualize o Microsoft Edge WebView2 Runtime e tente novamente.",
                ex);
        }

        var core = popoutView.CoreWebView2
            ?? throw new InvalidOperationException("O navegador interno não foi inicializado.");
        core.Settings.AreDefaultContextMenusEnabled = false;
        core.Settings.AreDevToolsEnabled = false;
        core.Settings.IsGeneralAutofillEnabled = false;
        core.Settings.IsPasswordAutosaveEnabled = false;
        core.Settings.IsStatusBarEnabled = false;
        core.Settings.IsZoomControlEnabled = false;
        core.Settings.IsBuiltInErrorPageEnabled = false;
        if (!_popoutResourceRequestHandlerRegistered)
        {
            foreach (var filter in TwitchVideoResourceFilters)
            {
                core.AddWebResourceRequestedFilter(
                    filter,
                    CoreWebView2WebResourceContext.All,
                    CoreWebView2WebResourceRequestSourceKinds.All);
            }
            foreach (var filter in TwitchTrackingResourceFilters)
            {
                core.AddWebResourceRequestedFilter(
                    filter,
                    CoreWebView2WebResourceContext.All,
                    CoreWebView2WebResourceRequestSourceKinds.All);
            }

            core.WebResourceRequested += TwitchPopout_WebResourceRequested;
            _popoutResourceRequestHandlerRegistered = true;
        }
        UpdateTwitchWebViewMemoryTarget();
        if (!_popoutScriptRegistered)
        {
            await core.AddScriptToExecuteOnDocumentCreatedAsync(TwitchMessagesOnlyScript);
            _popoutScriptRegistered = true;
        }
        if (!_twitchCaptureScriptRegistered)
        {
            await core.AddScriptToExecuteOnDocumentCreatedAsync(TwitchCaptureScript);
            core.WebMessageReceived += PublicChat_WebMessageReceived;
            _twitchCaptureScriptRegistered = true;
        }

        RegisterPopoutNavigationHandler(popoutView);
        _twitchPublicChatActive = true;
        popoutView.Visibility = Visibility.Visible;
        core.Navigate($"https://www.twitch.tv/popout/{Uri.EscapeDataString(channel)}/chat?popout=");
    }

    private WebView2 GetOrCreateTwitchPopoutView()
    {
        if (_twitchPopoutView is not null)
        {
            return _twitchPopoutView;
        }

        var popoutView = new WebView2
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            DefaultBackgroundColor = System.Drawing.Color.FromArgb(0, 0, 0, 0)
        };
        TwitchPopoutHost.Children.Add(popoutView);
        _twitchPopoutView = popoutView;
        return popoutView;
    }

    private void DisposeTwitchPopoutView()
    {
        var popoutView = _twitchPopoutView;
        if (popoutView is null)
        {
            _twitchPublicChatActive = false;
            return;
        }

        var core = popoutView.CoreWebView2;
        if (core is not null && _twitchCaptureScriptRegistered)
        {
            core.WebMessageReceived -= PublicChat_WebMessageReceived;
        }
        if (core is not null && _popoutResourceRequestHandlerRegistered)
        {
            core.WebResourceRequested -= TwitchPopout_WebResourceRequested;
        }

        if (_popoutNavigationHandlerRegistered)
        {
            popoutView.NavigationCompleted -= TwitchPopoutView_NavigationCompleted;
        }

        core?.Stop();
        popoutView.Visibility = Visibility.Collapsed;
        TwitchPopoutHost.Children.Remove(popoutView);
        _twitchPopoutView = null;
        _popoutScriptRegistered = false;
        _popoutNavigationHandlerRegistered = false;
        _popoutResourceRequestHandlerRegistered = false;
        _twitchCaptureScriptRegistered = false;
        _twitchPublicChatActive = false;
        popoutView.Dispose();
    }

    private void TwitchPopout_WebResourceRequested(
        object? sender,
        CoreWebView2WebResourceRequestedEventArgs e)
    {
        var environment = _twitchPopoutView?.CoreWebView2?.Environment;
        if (environment is null)
        {
            return;
        }

        e.Response = environment.CreateWebResourceResponse(
            null,
            204,
            "Blocked by TransparentChatFilter",
            "Content-Length: 0\r\n");
    }

    private void UpdateTwitchWebViewMemoryTarget()
    {
        var core = _twitchPopoutView?.CoreWebView2;
        if (core is null)
        {
            return;
        }

        core.MemoryUsageTargetLevel = IsActive && WindowState != WindowState.Minimized
            ? CoreWebView2MemoryUsageTargetLevel.Normal
            : CoreWebView2MemoryUsageTargetLevel.Low;
    }

    private void SetImageCacheCapacities(ChatConnectionMode connectionMode)
    {
        var isIrc = connectionMode == ChatConnectionMode.AuthenticatedIrc;
        _twitchEmoteImageCache.SetCapacity(isIrc ? IrcEmoteImageCacheCapacity : PublicEmoteImageCacheCapacity);
        _twitchBadgeImageCache.SetCapacity(isIrc ? IrcBadgeImageCacheCapacity : PublicBadgeImageCacheCapacity);
        _twitchChannelImageCache.SetCapacity(isIrc ? IrcChannelImageCacheCapacity : PublicChannelImageCacheCapacity);
    }

    private void RegisterPopoutNavigationHandler(WebView2 popoutView)
    {
        if (_popoutNavigationHandlerRegistered)
        {
            return;
        }

        popoutView.NavigationCompleted += TwitchPopoutView_NavigationCompleted;
        _popoutNavigationHandlerRegistered = true;
    }

    private void TwitchPopoutView_NavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        if (sender is not WebView2 popoutView ||
            !ReferenceEquals(popoutView, _twitchPopoutView) ||
            popoutView.Visibility != Visibility.Visible)
        {
            return;
        }

        if (e.IsSuccess)
        {
            if (_settings.ConnectionMode == ChatConnectionMode.PublicPopout)
            {
                _ = ApplyTwitchPublicChatScriptsAsync();
            }

            return;
        }

        SetConnected(false);
        _twitchPublicChatActive = false;
        var reason = e.WebErrorStatus == CoreWebView2WebErrorStatus.CertificateIsInvalid
            ? "O certificado da página não pôde ser validado."
            : $"A página pública do chat não carregou ({e.WebErrorStatus}).";
        popoutView.Visibility = Visibility.Collapsed;
        RefreshChatPanels();
        UpdateChatOnlyControlsVisibility();
        ViewModel.ConnectionStatus = "Falha ao carregar chat público";

        ViewModel.UpdateDiagnostic(reason);
    }

    private async Task ApplyTwitchPublicChatScriptsAsync()
    {
        var core = _twitchPopoutView?.CoreWebView2;
        if (core is null)
        {
            return;
        }

        try
        {
            await core.ExecuteScriptAsync(TwitchMessagesOnlyScript);
            await core.ExecuteScriptAsync(TwitchCaptureScript);
        }
        catch (System.Runtime.InteropServices.COMException ex)
        {
            ViewModel.UpdateDiagnostic($"Não foi possível iniciar a captura do chat da Twitch: {ex.Message}");
        }
    }

    private async Task ConnectAsync(bool showError = true, bool allowAuthorization = true)
    {
        if (Interlocked.CompareExchange(ref _connectInProgress, 1, 0) != 0)
        {
            if (_settings.ConnectionMode != ChatConnectionMode.PublicPopout ||
                _authorizationCts?.IsCancellationRequested != true)
            {
                return;
            }

            while (Volatile.Read(ref _connectInProgress) != 0)
            {
                await Task.Delay(50, _lifetimeCts.Token);
            }

            if (_settings.ConnectionMode != ChatConnectionMode.PublicPopout ||
                Interlocked.CompareExchange(ref _connectInProgress, 1, 0) != 0)
            {
                return;
            }
        }

        ViewModel.IsConnecting = true;
        try
        {
            ViewModel.ConnectionStatus = "Conectando...";
            var viewModel = ViewModel;
            var channels = GetChannelsToJoin(viewModel.Settings);
            if (channels.Count == 0)
            {
                throw new InvalidOperationException("Informe pelo menos um canal válido para monitorar.");
            }

            if (_settings.ConnectionMode == ChatConnectionMode.PublicPopout)
            {
                _chatClient.Disconnect();
                SetConnected(false);
                var channel = NormalizeChannel(viewModel.MainChannel);
                if (string.IsNullOrWhiteSpace(channel))
                {
                    throw new InvalidOperationException("Informe o canal da Twitch para abrir o chat público.");
                }

                await ShowTwitchPopoutAsync(channel);
                if (_twitchPopoutView is { } popoutView)
                {
                    popoutView.Visibility = Visibility.Visible;
                }
                viewModel.Messages.Clear();

                RefreshChatPanels();
                UpdateChatOnlyControlsVisibility();
                SetConnected(true);
                viewModel.ConnectionStatus = "Conectado";
                viewModel.UpdateDiagnostic($"Chat público da Twitch aberto para #{channel}; as mensagens são integradas ao feed do aplicativo.");
                RememberRecentChannel(channel);
                QueueSettingsSave();
                return;
            }

            ChatMessagesScrollViewer.Visibility = Visibility.Visible;
            DisposeTwitchPopoutView();
            RefreshChatPanels();
            var credentials = _twitchCredentials;
            if (credentials is not null)
            {
                try
                {
                    credentials = await _twitchOAuthClient.ValidateOrRefreshAsync(credentials, _lifetimeCts.Token);
                }
                catch (TwitchOAuthException) when (allowAuthorization)
                {
                    credentials = null;
                }
            }

            if (credentials is null && !allowAuthorization)
            {
                viewModel.ConnectionStatus = "Autorização necessária";
                _settingsWindow.TwitchAuthorizationStatus.Text = "Clique em Conectar para autorizar sua conta Twitch.";
                viewModel.UpdateDiagnostic("A conta Twitch ainda não foi autorizada. Clique em Conectar para iniciar a autorização segura.");
                return;
            }

            if (credentials is null)
            {
                credentials = await AuthorizeTwitchAsync();
            }

            TwitchCredentialStore.SaveCredentials(credentials);
            _twitchCredentials = credentials;
            UpdateConnectionModeUi();
            viewModel.TwitchUsername = credentials.Username;
            SetConnected(false);
            await _chatClient.ConnectAsync(channels, credentials.Username, credentials.AccessToken, _lifetimeCts.Token);
            SetConnected(true);
            viewModel.ConnectionStatus = "Conectado";
            viewModel.UpdateDiagnostic($"Conectado a: {string.Join(", ", channels.Select(channel => $"#{channel}"))}. O filtro de canais, usuários e comandos está ativo.");
            RememberRecentChannel(viewModel.MainChannel);
            QueueSettingsSave();
            _settingsWindow.TwitchAuthorizationStatus.Text = $"Autorizado como @{credentials.Username}.";
            _settingsWindow.TwitchDeviceCodeText.Visibility = Visibility.Collapsed;
            _settingsWindow.TwitchOpenAuthorizationButton.Visibility = Visibility.Collapsed;
        }
        catch (OperationCanceledException) when (_lifetimeCts.IsCancellationRequested || _authorizationCts?.IsCancellationRequested == true)
        {
            SetConnected(false);
            ViewModel.ConnectionStatus = "Autorização cancelada";
            _settingsWindow.TwitchAuthorizationStatus.Text = "Autorização cancelada.";
        }
        catch (Exception ex)
        {
            SetConnected(false);
            ViewModel.ConnectionStatus = "Falha na conexão";
            ViewModel.UpdateDiagnostic($"Falha ao conectar: {ex.Message}");
            if (showError && ex is not OperationCanceledException)
            {
                MessageBox.Show(
                    this,
                    $"Não foi possível conectar ao chat da Twitch.\n\n{ex.Message}",
                    "Falha na conexão",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }
        finally
        {
            _authorizationCts?.Dispose();
            _authorizationCts = null;
            _settingsWindow.TwitchCancelAuthorizationButton.Visibility = Visibility.Collapsed;
            Interlocked.Exchange(ref _connectInProgress, 0);
            ViewModel.IsConnecting = false;
            if (_channelUpdatePending)
            {
                _channelUpdatePending = false;
                _channelUpdateTimer.Start();
            }
        }
    }

    private async Task<TwitchCredentials> AuthorizeTwitchAsync()
    {
        var authorizationCts = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCts.Token);
        _authorizationCts = authorizationCts;
        _settingsWindow.TwitchAuthorizationStatus.Text = "Aguardando autorização na Twitch...";
        _settingsWindow.TwitchDeviceCodeText.Visibility = Visibility.Collapsed;
        _settingsWindow.TwitchOpenAuthorizationButton.Visibility = Visibility.Collapsed;
        _settingsWindow.TwitchCancelAuthorizationButton.Visibility = Visibility.Visible;

        var progress = new Progress<TwitchDeviceAuthorization>(deviceAuthorization =>
        {
            if (authorizationCts.IsCancellationRequested ||
                _settings.ConnectionMode != ChatConnectionMode.AuthenticatedIrc)
            {
                return;
            }

            _twitchVerificationUri = deviceAuthorization.VerificationUri;
            _settingsWindow.TwitchAuthorizationStatus.Text = $"Acesse a página da Twitch e informe este código (expira em {Math.Max(1, deviceAuthorization.ExpiresIn / 60)} min):";
            _settingsWindow.TwitchDeviceCodeText.Text = deviceAuthorization.UserCode;
            _settingsWindow.TwitchDeviceCodeText.Visibility = Visibility.Visible;
            _settingsWindow.TwitchOpenAuthorizationButton.Visibility = Visibility.Visible;
            ToggleSettingsPopup(true);
            ShowPreferencesPage(_settingsWindow.ConnectionsPreferencesPage, _settingsWindow.ConnectionsPreferencesButton);
            OpenTwitchAuthorizationPage();
        });

        return await _twitchOAuthClient.AuthorizeAsync(progress, authorizationCts.Token);
    }

    private void TwitchOpenAuthorizationButton_Click(object sender, RoutedEventArgs e)
    {
        OpenTwitchAuthorizationPage();
    }

    private void OpenTwitchAuthorizationPage()
    {
        if (_twitchVerificationUri is null)
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(_twitchVerificationUri.AbsoluteUri) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            ViewModel.UpdateDiagnostic($"Não foi possível abrir o navegador: {ex.Message}");
        }
    }

    private void TwitchCancelAuthorizationButton_Click(object sender, RoutedEventArgs e)
    {
        _authorizationCts?.Cancel();
    }

    private static List<string> GetChannelsToJoin(AppSettings settings)
    {
        static string Normalize(string value) => value.Trim().TrimStart('#').ToLowerInvariant();

        var mainChannel = Normalize(settings.MainChannel);
        var selectedChannels = settings.SelectedChannels
            .Select(Normalize)
            .Where(channel => !string.IsNullOrWhiteSpace(channel))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return settings.FilterMode switch
        {
            ChatFilterMode.OnlyMyChannel => string.IsNullOrWhiteSpace(mainChannel) ? new List<string>() : new List<string> { mainChannel },
            ChatFilterMode.JoinedChats => new[] { mainChannel }.Concat(selectedChannels)
                .Where(channel => !string.IsNullOrWhiteSpace(channel))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList(),
            _ => new List<string>()
        };
    }

    private async Task ReconnectAsync()
    {
        try
        {
            await Task.Delay(2000);
            if ((_isConnected || _ircReconnectPending) && !_chatClient.IsConnected)
            {
                await ConnectAsync(showError: false, allowAuthorization: false);
            }
        }
        finally
        {
            _ircReconnectPending = false;
            Interlocked.Exchange(ref _reconnectScheduled, 0);
        }
    }

    private void ApplyAppearance()
    {
        var chatOnly = _settings.IsTransparent;
        ResizeMode = ResizeMode.CanResize;
        ResizeGripIcon.Visibility = chatOnly ? Visibility.Collapsed : Visibility.Visible;
        var trackChatOnlyGear = chatOnly && _settings.ClickThroughEnabled;
        if (trackChatOnlyGear)
        {
            _chatOnlyHitTestTimer.Start();
        }
        else
        {
            _chatOnlyHitTestTimer.Stop();
            _cursorOverChatOnlyGear = false;
        }

        UpdateChatOnlyControlsVisibility();
        var alpha = (byte)Math.Round(255 * Math.Clamp(_settings.BackgroundOpacity, 0.15, 1.0));
        RootBorder.Background = Brushes.Transparent;
        var surfaceBorderBrush = chatOnly
            ? Brushes.Transparent
            : new SolidColorBrush(Color.FromArgb(120, 120, 150, 180));
        var surfaceBorderThickness = chatOnly ? new Thickness(0) : new Thickness(1);
        var surfaceCornerRadius = chatOnly ? new CornerRadius(0) : new CornerRadius(18);
        RootBorder.BorderBrush = surfaceBorderBrush;
        RootBorder.BorderThickness = surfaceBorderThickness;
        RootBorder.CornerRadius = surfaceCornerRadius;
        WindowShadow.BorderBrush = surfaceBorderBrush;
        WindowShadow.BorderThickness = surfaceBorderThickness;
        WindowShadow.CornerRadius = surfaceCornerRadius;
        WindowShadow.Visibility = chatOnly ? Visibility.Collapsed : Visibility.Visible;
        MainLayoutGrid.Margin = chatOnly ? new Thickness(0) : new Thickness(8);
        Toolbar.Visibility = chatOnly ? Visibility.Collapsed : Visibility.Visible;
        var chatBackground = _settings.ConnectionMode == ChatConnectionMode.AuthenticatedIrc
            ? Color.FromRgb(24, 24, 27)
            : Color.FromRgb(13, 20, 32);
        ChatContainer.Background = chatOnly
            ? Brushes.Transparent
            : new SolidColorBrush(Color.FromArgb(alpha, chatBackground.R, chatBackground.G, chatBackground.B));
        ChatContainer.BorderBrush = chatOnly
            ? Brushes.Transparent
            : new SolidColorBrush(Color.FromRgb(45, 55, 71));
        ChatContainer.BorderThickness = chatOnly ? new Thickness(0) : new Thickness(1);
        ChatContainer.CornerRadius = chatOnly ? new CornerRadius(0) : new CornerRadius(12);
        ChatMessagesScrollViewer.Padding = chatOnly ? new Thickness(0, 38, 0, 0) : new Thickness(0);
        if (_twitchPopoutView is not null)
        {
            _twitchPopoutView.Margin = chatOnly ? new Thickness(0, 38, 0, 0) : new Thickness(0);
        }
        Toolbar.Opacity = 0.82;
        UpdateClickThroughStyle();
    }

    private static bool ShouldDisplayMessage(ChatMessage message, AppSettings settings)
    {
        var origin = NormalizeChannel(message.EffectiveOrigin);
        var main = NormalizeChannel(settings.MainChannel);
        var selected = settings.SelectedChannels.Select(NormalizeChannel).Where(x => !string.IsNullOrEmpty(x)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var blocked = settings.BlockedChannels.Select(NormalizeChannel).Where(x => !string.IsNullOrEmpty(x)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var ircFiltersEnabled = settings.ConnectionMode == ChatConnectionMode.AuthenticatedIrc;
        var blockedUsers = settings.BlockedUsers
            .Select(user => user.Trim().TrimStart('@').ToLowerInvariant())
            .Where(user => !string.IsNullOrEmpty(user))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (blockedUsers.Contains(message.LoginName.Trim().TrimStart('@')) ||
            blockedUsers.Contains(message.DisplayName.Trim().TrimStart('@')))
        {
            return false;
        }

        if (ircFiltersEnabled && settings.HideCommands && message.Message.TrimStart().StartsWith('!'))
        {
            return false;
        }

        if (ContainsAnyTerm($"{message.SystemText} {message.Message}", settings.BlockedTerms))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(origin))
        {
            return false;
        }

        if (settings.FilterMode == ChatFilterMode.OnlyMyChannel &&
            !string.IsNullOrWhiteSpace(message.SourceRoomId) &&
            !string.IsNullOrWhiteSpace(message.RoomId) &&
            !string.Equals(message.SourceRoomId, message.RoomId, StringComparison.Ordinal))
        {
            return false;
        }

        if (ircFiltersEnabled && blocked.Contains(origin))
        {
            return false;
        }

        return settings.FilterMode switch
        {
            ChatFilterMode.OnlyMyChannel => string.Equals(origin, main, StringComparison.OrdinalIgnoreCase),
            ChatFilterMode.JoinedChats => string.Equals(origin, main, StringComparison.OrdinalIgnoreCase) || selected.Contains(origin),
            _ => false
        };
    }

    private static bool ContainsAnyTerm(string text, string? configuredTerms)
    {
        return ParseTerms(configuredTerms)
            .Any(term => text.Contains(term, StringComparison.OrdinalIgnoreCase));
    }

    private static string[] ParseTerms(string? configuredTerms)
    {
        return (configuredTerms ?? string.Empty)
            .Split([',', ';', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(term => !string.IsNullOrWhiteSpace(term))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string NormalizeChannel(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var trimmed = value.Trim();
        if (trimmed.StartsWith('#'))
        {
            trimmed = trimmed[1..];
        }

        return trimmed.Trim().ToLowerInvariant();
    }

    private static SolidColorBrush CreateMessageBackgroundBrush(AppSettings settings)
    {
        var color = ParseHexColor(settings.MessageBackgroundColor) ?? Color.FromRgb(22, 29, 44);
        var baseAlpha = settings.IsTransparent ? 184 : 255;
        var opacity = settings.UseMessageBackgroundOpacity
            ? settings.MessageBackgroundOpacity
            : 0.0;
        var alpha = (byte)Math.Round(baseAlpha * opacity);
        return new SolidColorBrush(Color.FromArgb(alpha, color.R, color.G, color.B));
    }

    private static bool IsOtherChannelMessage(ChatMessage message, AppSettings settings)
    {
        if (!string.IsNullOrWhiteSpace(message.SourceRoomId) &&
            !string.IsNullOrWhiteSpace(message.RoomId))
        {
            return !string.Equals(message.SourceRoomId, message.RoomId, StringComparison.Ordinal);
        }

        var origin = NormalizeChannel(message.EffectiveOrigin);
        var main = NormalizeChannel(settings.MainChannel);
        return !string.IsNullOrWhiteSpace(origin) &&
               !string.IsNullOrWhiteSpace(main) &&
               !string.Equals(origin, main, StringComparison.OrdinalIgnoreCase);
    }

    private ChatRowView CreateChatRow(
        ChatMessage message,
        AppSettings settings,
        bool isNewMessage = false)
    {
        var color = settings.UseTwitchNameColors
            ? TryParseColor(message.UsernameColor) ??
              (settings.ConnectionMode == ChatConnectionMode.AuthenticatedIrc
                  ? GetTwitchUsernameColor(message.LoginName, message.DisplayName)
                  : Colors.White)
            : Colors.White;
        var badges = settings.ShowBadges
            ? message.Badges
                .Where(badge =>
                    (settings.ShowModerators || !string.Equals(badge.SetId, "moderator", StringComparison.OrdinalIgnoreCase)) &&
                    (settings.ShowVips || !string.Equals(badge.SetId, "vip", StringComparison.OrdinalIgnoreCase)))
                .ToArray()
            : Array.Empty<TwitchChatBadge>();

        var row = new ChatRowView
        {
            SourceMessage = message,
            Emotes = message.Emotes,
            IsSystemEvent = message.Kind != ChatMessageKind.Chat,
            IsNewMessage = isNewMessage,
            IsAuthenticatedIrc = settings.ConnectionMode == ChatConnectionMode.AuthenticatedIrc,
            IsTransparent = settings.IsTransparent,
            ShowChatTextOutline = settings.ShowChatTextOutline,
            FontSize = settings.FontSize,
            LineHeight = settings.FontSize * 1.3,
            TextOpacity = settings.TextOpacity,
            EventAccentBrush = GetSystemEventAccent(message.Kind),
            MaxWidth = Math.Max(
                0,
                ChatMessagesScrollViewer.ActualWidth - GetMessageHorizontalPadding(settings)),
            SystemText = message.Kind == ChatMessageKind.Chat ? string.Empty : message.SystemText,
            RowMargin = new Thickness(0, 0, 0, settings.IsTransparent ? 2 : settings.MessageSpacing),
            PlatformImage = ChatRowView.TwitchImage,
            ChannelImageUrl = settings.ConnectionMode == ChatConnectionMode.PublicPopout ||
                              settings.FilterMode == ChatFilterMode.JoinedChats
                ? message.ChannelImageUrl
                : string.Empty,
            MessageBackgroundBrush = CreateMessageBackgroundBrush(settings),
            RowOpacity = settings.ConnectionMode == ChatConnectionMode.AuthenticatedIrc &&
                         settings.DimOtherChannelMessages &&
                         IsOtherChannelMessage(message, settings)
                ? 0.5
                : 1,
            PlatformName = "Twitch",
            Badges = badges,
            FullTimestampText = settings.ShowTimestamps
                ? $"{message.TimestampUtc.ToLocalTime():HH:mm} "
                : string.Empty,
            DisplayName = settings.ShowUserNames && message.Kind == ChatMessageKind.Chat ? message.DisplayName : string.Empty,
            BadgeText = string.Empty,
            NameSeparator = settings.ShowUserNames && message.Kind == ChatMessageKind.Chat && !settings.UserNameAboveMessage ? ": " : string.Empty,
            UserNameAboveMessage = settings.UserNameAboveMessage && settings.ShowUserNames && message.Kind == ChatMessageKind.Chat,
            DefaultNameBrush = new SolidColorBrush(settings.HighlightedUsers.Contains(GetMessageUserName(message), StringComparer.OrdinalIgnoreCase)
                ? Color.FromRgb(255, 209, 102)
                : color),
            Text = message.Message
        };
        row.SetTransparentAppearance(settings.IsTransparent);
        return row;
    }

    private static double GetMessageHorizontalPadding(AppSettings settings)
    {
        const double eventStripeAndTextInset = 11;
        var borderPadding = settings.IsTransparent ? 10 : 16;
        return borderPadding + eventStripeAndTextInset;
    }

    private Brush GetSystemEventAccent(ChatMessageKind kind)
    {
        var resourceKey = kind switch
        {
            ChatMessageKind.Subscription or ChatMessageKind.Resubscription or ChatMessageKind.GiftSubscription => "SubscriptionEventBrush",
            ChatMessageKind.Raid => "RaidEventBrush",
            ChatMessageKind.Bits => "BitsEventBrush",
            ChatMessageKind.Announcement => "AnnouncementEventBrush",
            _ => "AccentBrush"
        };
        return (Brush)FindResource(resourceKey);
    }

    private void ChatMessageTextBlock_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            RenderChatMessageTextBlock(sender);
            if (sender is TextBlock textBlock && textBlock.DataContext is ChatRowView row)
            {
                AnimateNewChatMessage(textBlock, row);
            }
        }
        catch (Exception ex) when (
            ex is InvalidOperationException or ArgumentException or FormatException or NotSupportedException)
        {
            ViewModel.UpdateDiagnostic($"Não foi possível renderizar uma mensagem do chat: {ex.Message}");
        }
    }

    private void ChatMessageHeaderTextBlock_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not TextBlock textBlock || textBlock.DataContext is not ChatRowView row)
        {
            return;
        }

        try
        {
            textBlock.Inlines.Clear();
            if (!string.IsNullOrWhiteSpace(row.ChannelImageUrl))
            {
                textBlock.Inlines.Add(CreateTwitchChannelImageInline(row));
            }

            foreach (var badge in row.Badges)
            {
                textBlock.Inlines.Add(CreateTwitchBadgeInline(badge, VisualTreeHelper.GetDpi(textBlock).DpiScaleX));
            }

            textBlock.Inlines.Add(new Run(row.DisplayName)
            {
                FontWeight = FontWeights.Bold,
                Foreground = row.NameBrush
            });
        }
        catch (Exception ex) when (
            ex is InvalidOperationException or ArgumentException or FormatException or NotSupportedException)
        {
            ViewModel.UpdateDiagnostic($"Não foi possível renderizar o nome e os badges da mensagem: {ex.Message}");
        }
    }

    private static void AnimateNewChatMessage(TextBlock textBlock, ChatRowView row)
    {
        if (!row.IsNewMessage)
        {
            return;
        }

        row.IsNewMessage = false;
        if (!SystemParameters.ClientAreaAnimation)
        {
            return;
        }

        DependencyObject? current = textBlock;
        while (current is not null && current is not ContentPresenter)
        {
            current = VisualTreeHelper.GetParent(current);
        }

        if (current is not ContentPresenter presenter)
        {
            return;
        }

        presenter.Opacity = 0;
        var transform = new TranslateTransform(0, 6);
        presenter.RenderTransform = transform;
        presenter.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(160))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        });
        transform.BeginAnimation(
            TranslateTransform.YProperty,
            new DoubleAnimation(6, 0, TimeSpan.FromMilliseconds(160))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            });
    }

    private void RenderChatMessageTextBlock(object sender)
    {
        if (sender is not TextBlock textBlock || textBlock.DataContext is not ChatRowView row)
        {
            return;
        }

        textBlock.Inlines.Clear();
        if (row.IsSystemEvent)
        {
            var systemRun = new Run(row.SystemText)
            {
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(ParseHexColor(_settings.HighlightColor) ?? Color.FromRgb(94, 162, 255))
            };
            textBlock.Inlines.Add(systemRun);
            if (!string.IsNullOrWhiteSpace(row.SourceMessage.Message))
            {
                textBlock.Inlines.Add(new Run(" "));
                AddMessageBodyInlines(textBlock, row);
            }

            return;
        }

        if (!string.IsNullOrEmpty(row.TimestampText))
        {
            textBlock.Inlines.Add(new Run(row.TimestampText)
            {
                Foreground = new SolidColorBrush(Color.FromRgb(155, 176, 200)),
                FontSize = 12
            });
        }

        if (!row.UserNameAboveMessage && !string.IsNullOrWhiteSpace(row.ChannelImageUrl))
        {
            textBlock.Inlines.Add(CreateTwitchChannelImageInline(row));
        }

        if (!row.UserNameAboveMessage)
        {
            foreach (var badge in row.Badges)
            {
                textBlock.Inlines.Add(CreateTwitchBadgeInline(badge, VisualTreeHelper.GetDpi(textBlock).DpiScaleX));
            }
        }

        if (!string.IsNullOrEmpty(row.DisplayName) && !row.UserNameAboveMessage)
        {
            textBlock.Inlines.Add(new Run(row.DisplayName)
            {
                FontWeight = FontWeights.Bold,
                Foreground = row.NameBrush
            });
            textBlock.Inlines.Add(new Run(row.NameSeparator)
            {
                Foreground = new SolidColorBrush(GetChatTextColor())
            });
        }

        AddMessageBodyInlines(textBlock, row);
    }

    private InlineUIContainer CreateInlineImage(ImageSource source, string toolTip, double size)
    {
        var image = new Image
        {
            Source = source,
            Width = size,
            Height = size,
            Stretch = Stretch.Uniform,
            ToolTip = toolTip,
            Margin = new Thickness(0, 0, 3, 0)
        };
        return new InlineUIContainer(image) { BaselineAlignment = BaselineAlignment.Center };
    }

    private InlineUIContainer CreateTwitchBadgeInline(TwitchChatBadge badge, double dpiScale)
    {
        if (string.IsNullOrWhiteSpace(badge.ImageUrl))
        {
            return CreateBadgeTextFallback(badge);
        }

        var (imageUrl, decodePixelWidth) = GetBadgeImageForDpi(badge, dpiScale);
        var image = new Image
        {
            Width = 16,
            Height = 16,
            Stretch = Stretch.Uniform,
            ToolTip = badge.Title,
            Margin = new Thickness(0, 0, 3, 0)
        };
        var inline = new InlineUIContainer(CreateBadgeTextFallbackText(badge))
        {
            BaselineAlignment = BaselineAlignment.Center
        };
        _ = LoadTwitchBadgeImageAsync(inline, image, badge, imageUrl, decodePixelWidth);
        return inline;
    }

    private static (string ImageUrl, int DecodePixelWidth) GetBadgeImageForDpi(
        TwitchChatBadge badge,
        double dpiScale)
    {
        if (dpiScale > 2 && !string.IsNullOrWhiteSpace(badge.ImageUrl4x))
        {
            return (badge.ImageUrl4x, 64);
        }

        if (dpiScale > 1 && !string.IsNullOrWhiteSpace(badge.ImageUrl2x))
        {
            return (badge.ImageUrl2x, 32);
        }

        return (badge.ImageUrl, 16);
    }

    private static InlineUIContainer CreateBadgeTextFallback(TwitchChatBadge badge)
    {
        return new InlineUIContainer(CreateBadgeTextFallbackText(badge))
        {
            BaselineAlignment = BaselineAlignment.Center
        };
    }

    private static TextBlock CreateBadgeTextFallbackText(TwitchChatBadge badge)
    {
        var label = badge.SetId.ToLowerInvariant() switch
        {
            "moderator" => "MOD",
            "vip" => "VIP",
            "subscriber" => "SUB",
            "broadcaster" => "LIVE",
            _ => badge.SetId.Length > 5 ? badge.SetId[..5].ToUpperInvariant() : badge.SetId.ToUpperInvariant()
        };
        var text = new TextBlock
        {
            Text = label,
            FontSize = 12,
            FontWeight = FontWeights.Bold,
            Foreground = Brushes.White,
            Background = new SolidColorBrush(Color.FromRgb(74, 55, 98)),
            Padding = new Thickness(2, 1, 2, 1),
            ToolTip = badge.Title,
            Margin = new Thickness(0, 0, 3, 0)
        };
        return text;
    }

    private void AddMessageBodyInlines(TextBlock textBlock, ChatRowView row)
    {
        var text = row.SourceMessage.Message;
        var position = 0;
        var isAction = row.SourceMessage.IsAction;
        var terms = isAction ? Array.Empty<string>() : ParseTerms(_settings.HighlightTerms);
        var actionBrush = isAction ? row.NameBrush : null;
        var bodyFontStyle = isAction ? FontStyles.Italic : FontStyles.Normal;
        foreach (var emote in row.Emotes)
        {
            if (emote.Start < position || emote.End >= text.Length)
            {
                continue;
            }

            AddHighlightedText(textBlock, text[position..emote.Start], terms, actionBrush, bodyFontStyle);
            var fallbackText = text[emote.Start..(emote.End + 1)];
            var image = new Image
            {
                Width = Math.Max(18, ViewModel.FontSize * 1.6),
                Height = Math.Max(18, ViewModel.FontSize * 1.6),
                Stretch = Stretch.Uniform,
                ToolTip = fallbackText,
                Margin = new Thickness(1, 0, 1, 0)
            };
            var emoteInline = new InlineUIContainer(image) { BaselineAlignment = BaselineAlignment.Center };
            textBlock.Inlines.Add(emoteInline);
            var decodePixelWidth = Math.Max(
                1,
                (int)Math.Ceiling(image.Width * VisualTreeHelper.GetDpi(textBlock).DpiScaleX));
            _ = LoadTwitchEmoteImageAsync(emoteInline, image, emote.Id, fallbackText, decodePixelWidth, actionBrush, bodyFontStyle);
            position = emote.End + 1;
        }

        AddHighlightedText(textBlock, text[position..], terms, actionBrush, bodyFontStyle);
    }

    private async Task LoadTwitchEmoteImageAsync(
        InlineUIContainer inline,
        Image image,
        string emoteId,
        string fallbackText,
        int decodePixelWidth,
        Brush? foreground,
        FontStyle fontStyle)
    {
        try
        {
            var imageUrl = $"https://static-cdn.jtvnw.net/emoticons/v2/{Uri.EscapeDataString(emoteId)}/default/dark/2.0";
            var emoteImage = await _twitchEmoteImageCache.GetOrCreateAsync(
                imageUrl,
                () => DownloadTwitchEmoteImageAsync(imageUrl, decodePixelWidth, _lifetimeCts.Token),
                _lifetimeCts.Token);
            emoteImage.ApplyTo(image);
        }
        catch (Exception ex) when (IsImageLoadFailure(ex))
        {
            if (!_lifetimeCts.IsCancellationRequested)
            {
                inline.Child = new TextBlock
                {
                    Text = fallbackText,
                    FontSize = ViewModel.FontSize,
                    Foreground = foreground ?? new SolidColorBrush(GetChatTextColor()),
                    FontStyle = fontStyle
                };
                ViewModel.UpdateDiagnostic($"Não foi possível carregar o emote da Twitch: {ex.Message}");
            }
        }
    }

    private async Task LoadTwitchBadgeImageAsync(
        InlineUIContainer inline,
        Image image,
        TwitchChatBadge badge,
        string imageUrl,
        int decodePixelWidth)
    {
        try
        {
            image.Source = await _twitchBadgeImageCache.GetOrCreateAsync(
                $"{imageUrl}|{decodePixelWidth}",
                () => DownloadTwitchImageAsync(imageUrl, decodePixelWidth, _lifetimeCts.Token),
                _lifetimeCts.Token);
            inline.Child = image;
        }
        catch (Exception ex) when (IsImageLoadFailure(ex))
        {
            if (!_lifetimeCts.IsCancellationRequested)
            {
                ReportBadgeErrorOncePerMinute($"Não foi possível carregar imagens de badges da Twitch: {ex.Message}");
            }
        }
    }

    private static bool IsImageLoadFailure(Exception exception) =>
        exception is HttpRequestException or IOException or InvalidDataException or NotSupportedException or
            OperationCanceledException or FormatException or ArgumentException or InvalidOperationException;

    private void ReportBadgeErrorOncePerMinute(string message)
    {
        var now = DateTimeOffset.UtcNow.UtcTicks;
        while (true)
        {
            var lastReported = Volatile.Read(ref _lastBadgeImageErrorReportedUtcTicks);
            if (lastReported != 0 && now - lastReported < TimeSpan.FromMinutes(1).Ticks)
            {
                return;
            }

            if (Interlocked.CompareExchange(ref _lastBadgeImageErrorReportedUtcTicks, now, lastReported) == lastReported)
            {
                ViewModel.UpdateDiagnostic(message);
                return;
            }
        }
    }

    private static async Task<ImageSource> DownloadTwitchImageAsync(
        string imageUrl,
        int decodePixelWidth,
        CancellationToken applicationCancellationToken)
    {
        var imageBytes = await DownloadTwitchImageBytesAsync(imageUrl, applicationCancellationToken);
        using var stream = new MemoryStream(imageBytes);
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.DecodePixelWidth = decodePixelWidth;
        image.StreamSource = stream;
        image.EndInit();
        image.Freeze();
        return image;
    }

    private static async Task<TwitchEmoteImageAsset> DownloadTwitchEmoteImageAsync(
        string imageUrl,
        int decodePixelWidth,
        CancellationToken applicationCancellationToken)
    {
        var imageBytes = await DownloadTwitchImageBytesAsync(imageUrl, applicationCancellationToken);
        return TwitchEmoteImageAsset.Decode(imageBytes, decodePixelWidth);
    }

    private static async Task<byte[]> DownloadTwitchImageBytesAsync(
        string imageUrl,
        CancellationToken applicationCancellationToken)
    {
        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
            applicationCancellationToken,
            timeoutCts.Token);
        using var request = new HttpRequestMessage(HttpMethod.Get, imageUrl);
        request.Headers.UserAgent.ParseAdd("TransparentChatFilter/1.0");
        using var response = await TwitchImageHttpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            linkedCts.Token);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsByteArrayAsync(linkedCts.Token);
    }

    private InlineUIContainer CreateTwitchChannelImageInline(ChatRowView row)
    {
        var image = new Image
        {
            Width = 16,
            Height = 16,
            Stretch = Stretch.Uniform,
            ToolTip = "Canal em que a mensagem foi enviada",
            Margin = new Thickness(0, 0, 3, 0)
        };
        var inline = new InlineUIContainer(image) { BaselineAlignment = BaselineAlignment.Center };
        _ = LoadTwitchChannelImageAsync(inline, image, row.ChannelImageUrl);

        return inline;
    }

    private async Task LoadTwitchChannelImageAsync(InlineUIContainer inline, Image image, string imageUrl)
    {
        try
        {
            image.Source = await _twitchChannelImageCache.GetOrCreateAsync(
                imageUrl,
                () => DownloadTwitchImageAsync(imageUrl, 32, _lifetimeCts.Token),
                _lifetimeCts.Token);
        }
        catch (Exception ex) when (IsImageLoadFailure(ex))
        {
            inline.Child = null;
            if (!_lifetimeCts.IsCancellationRequested)
            {
                ViewModel.UpdateDiagnostic($"Não foi possível carregar a foto do canal Twitch: {ex.Message}");
            }
        }
    }

    private void AddHighlightedText(
        TextBlock textBlock,
        string text,
        IReadOnlyList<string> terms,
        Brush? foreground = null,
        FontStyle? fontStyle = null)
    {
        if (text.Length == 0)
        {
            return;
        }

        var accent = ParseHexColor(_settings.HighlightColor) ?? Color.FromRgb(94, 162, 255);
        var matchRanges = terms
            .SelectMany(term => FindTermMatches(text, term))
            .OrderBy(range => range.Start)
            .ThenByDescending(range => range.Length)
            .ToArray();
        var cursor = 0;
        foreach (var match in matchRanges)
        {
            if (match.Start < cursor)
            {
                continue;
            }

            if (match.Start > cursor)
            {
                textBlock.Inlines.Add(new Run(text[cursor..match.Start])
                {
                    Foreground = foreground ?? new SolidColorBrush(GetChatTextColor()),
                    FontStyle = fontStyle ?? FontStyles.Normal
                });
            }

            textBlock.Inlines.Add(CreateHighlightedTermInline(
                text.Substring(match.Start, match.Length),
                accent,
                textBlock.FontSize));
            cursor = match.Start + match.Length;
        }

        if (cursor < text.Length)
        {
            textBlock.Inlines.Add(new Run(text[cursor..])
            {
                Foreground = foreground ?? new SolidColorBrush(GetChatTextColor()),
                FontStyle = fontStyle ?? FontStyles.Normal
            });
        }
    }

    private static InlineUIContainer CreateHighlightedTermInline(string text, Color accent, double fontSize)
    {
        var label = new TextBlock
        {
            Text = text,
            FontSize = fontSize,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(GetContrastingTextColor(accent)),
            TextWrapping = TextWrapping.NoWrap
        };
        var highlight = new Border
        {
            Background = new SolidColorBrush(accent),
            BorderBrush = new SolidColorBrush(Color.FromArgb(220, accent.R, accent.G, accent.B)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(3, 0, 3, 0),
            Margin = new Thickness(1, 0, 1, 0),
            Child = label
        };
        return new InlineUIContainer(highlight) { BaselineAlignment = BaselineAlignment.Center };
    }

    private static Color GetContrastingTextColor(Color background)
    {
        static double Linearize(byte component)
        {
            var value = component / 255.0;
            return value <= 0.04045 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
        }

        var luminance = 0.2126 * Linearize(background.R) +
                        0.7152 * Linearize(background.G) +
                        0.0722 * Linearize(background.B);
        var whiteContrast = 1.05 / (luminance + 0.05);
        var blackContrast = (luminance + 0.05) / 0.05;
        return whiteContrast >= blackContrast ? Colors.White : Colors.Black;
    }

    private static IEnumerable<(int Start, int Length)> FindTermMatches(string text, string term)
    {
        var start = 0;
        while ((start = text.IndexOf(term, start, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            yield return (start, term.Length);
            start += term.Length;
        }
    }

    private Color GetChatTextColor()
    {
        return Color.FromRgb(239, 239, 241);
    }

    private static Color? ParseHexColor(string rawColor)
    {
        try
        {
            return ColorConverter.ConvertFromString(rawColor) is Color color ? color : null;
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static Color? TryParseColor(string rawColor)
    {
        if (!string.IsNullOrWhiteSpace(rawColor) && rawColor.StartsWith("#") && rawColor.Length == 7)
        {
            return ParseHexColor(rawColor);
        }

        return null;
    }

    private static Color GetTwitchUsernameColor(string loginName, string displayName)
    {
        Color[] palette =
        [
            Color.FromRgb(255, 107, 107),
            Color.FromRgb(105, 168, 255),
            Color.FromRgb(105, 203, 119),
            Color.FromRgb(255, 140, 102),
            Color.FromRgb(181, 155, 255),
            Color.FromRgb(255, 200, 87),
            Color.FromRgb(77, 208, 225),
            Color.FromRgb(255, 111, 177),
            Color.FromRgb(190, 220, 85),
            Color.FromRgb(255, 125, 190),
            Color.FromRgb(94, 218, 170),
            Color.FromRgb(255, 178, 92)
        ];

        var name = string.IsNullOrWhiteSpace(loginName) ? displayName : loginName;
        uint hash = 2166136261;
        foreach (var character in name)
        {
            hash = (hash ^ char.ToLowerInvariant(character)) * 16777619;
        }

        return palette[(int)(hash % (uint)palette.Length)];
    }

}
