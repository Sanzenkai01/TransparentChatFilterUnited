using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Text.Json;
using Microsoft.Win32;
using Microsoft.Web.WebView2.Core;

namespace TransparentChatFilter;

public sealed class OverlayViewModel : INotifyPropertyChanged
{
    private readonly AppSettings _settings;

    public OverlayViewModel(AppSettings settings)
    {
        _settings = settings;
        Messages = new ObservableCollection<ChatRowView>();
        MainChannel = settings.MainChannel;
        ConnectionStatus = "Desconectado";
        SelectedChannelsText = string.Join(", ", settings.SelectedChannels);
        BlockedChannelsText = string.Join(", ", settings.BlockedChannels);
        BlockedUsersText = string.Join(", ", settings.BlockedUsers);
        SettingsVisibility = settings.ShowSettingsPanel ? Visibility.Visible : Visibility.Collapsed;
    }

    public int FontSize
    {
        get => _settings.FontSize;
        set
        {
            var fontSize = Math.Clamp(value, 10, 32);
            if (_settings.FontSize == fontSize)
            {
                return;
            }

            _settings.FontSize = fontSize;
            OnPropertyChanged();
        }
    }

    public ObservableCollection<ChatRowView> Messages { get; }

    public AppSettings Settings => _settings;

    public string MainChannel
    {
        get => _settings.MainChannel;
        set
        {
            if (string.Equals(_settings.MainChannel, value, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            _settings.MainChannel = NormalizedChannel(value);
            OnPropertyChanged();
        }
    }

    public string YouTubeVideoId
    {
        get => _settings.YouTubeVideoId;
        set
        {
            var normalized = value.Trim();
            if (string.Equals(_settings.YouTubeVideoId, normalized, StringComparison.Ordinal))
            {
                return;
            }

            _settings.YouTubeVideoId = normalized;
            OnPropertyChanged();
        }
    }

    public string TwitchUsername
    {
        get => _settings.TwitchUsername;
        set
        {
            var normalized = value.Trim().TrimStart('#').ToLowerInvariant();
            if (string.Equals(_settings.TwitchUsername, normalized, StringComparison.Ordinal))
            {
                return;
            }

            _settings.TwitchUsername = normalized;
            OnPropertyChanged();
        }
    }

    public ChatConnectionMode ConnectionMode
    {
        get => _settings.ConnectionMode;
        set
        {
            if (_settings.ConnectionMode == value)
            {
                return;
            }

            _settings.ConnectionMode = value;
            OnPropertyChanged();
        }
    }

    public string SelectedChannelsText
    {
        get => string.Join(", ", _settings.SelectedChannels);
        set
        {
            _settings.SelectedChannels = ParseList(value);
            OnPropertyChanged();
        }
    }

    public string BlockedChannelsText
    {
        get => string.Join(", ", _settings.BlockedChannels);
        set
        {
            _settings.BlockedChannels = ParseList(value);
            OnPropertyChanged();
        }
    }

    public string BlockedUsersText
    {
        get => string.Join(", ", _settings.BlockedUsers);
        set
        {
            _settings.BlockedUsers = ParseUserList(value);
            OnPropertyChanged();
        }
    }

    private string _connectionStatus = string.Empty;
    private bool _isConnected;
    private bool _isTwitchConnected;

    public bool IsConnected
    {
        get => _isConnected;
        set
        {
            if (_isConnected == value)
            {
                return;
            }

            _isConnected = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ConnectButtonText));
        }
    }

    public bool IsTwitchConnected
    {
        get => _isTwitchConnected;
        set
        {
            if (_isTwitchConnected == value)
            {
                return;
            }

            _isTwitchConnected = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ConnectButtonText));
        }
    }

    public string ConnectButtonText => IsTwitchConnected ? "Desconectar" : "Conectar";

    public string ConnectionStatus
    {
        get => _connectionStatus;
        set
        {
            if (_connectionStatus == value)
            {
                return;
            }

            _connectionStatus = value;
            OnPropertyChanged();
        }
    }

    public string LastDiagnostic { get; set; } = "Nenhum diagnóstico registrado.";

    public ChatFilterMode FilterMode
    {
        get => _settings.FilterMode;
        set
        {
            _settings.FilterMode = value;
            OnPropertyChanged();
        }
    }

    public bool ShowTimestamps
    {
        get => _settings.ShowTimestamps;
        set
        {
            _settings.ShowTimestamps = value;
            OnPropertyChanged();
        }
    }

    public bool ShowBadges
    {
        get => _settings.ShowBadges;
        set
        {
            _settings.ShowBadges = value;
            OnPropertyChanged();
        }
    }

    public bool ShowUserNames
    {
        get => _settings.ShowUserNames;
        set
        {
            _settings.ShowUserNames = value;
            OnPropertyChanged();
        }
    }

    public bool ShowModerators
    {
        get => _settings.ShowModerators;
        set
        {
            _settings.ShowModerators = value;
            OnPropertyChanged();
        }
    }

    public bool ShowVips
    {
        get => _settings.ShowVips;
        set
        {
            _settings.ShowVips = value;
            OnPropertyChanged();
        }
    }

    public bool HideCommands
    {
        get => _settings.HideCommands;
        set
        {
            _settings.HideCommands = value;
            OnPropertyChanged();
        }
    }

    public bool ClickThroughEnabled
    {
        get => _settings.ClickThroughEnabled;
        set
        {
            _settings.ClickThroughEnabled = value;
            OnPropertyChanged();
        }
    }

    public bool IsTransparent
    {
        get => _settings.IsTransparent;
        set
        {
            _settings.IsTransparent = value;
            OnPropertyChanged();
        }
    }

    public bool StartMinimized
    {
        get => _settings.StartMinimized;
        set
        {
            _settings.StartMinimized = value;
            OnPropertyChanged();
        }
    }

    public Visibility SettingsVisibility { get; set; }

    public double BackgroundOpacity
    {
        get => _settings.BackgroundOpacity;
        set
        {
            _settings.BackgroundOpacity = value;
            OnPropertyChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public void SetSettingsVisible(bool visible)
    {
        SettingsVisibility = visible ? Visibility.Visible : Visibility.Collapsed;
        _settings.ShowSettingsPanel = visible;
        OnPropertyChanged(nameof(SettingsVisibility));
    }

    public void SaveSettings() => _settings.Save();

    public void UpdateDiagnostic(string message)
    {
        LastDiagnostic = message;
        OnPropertyChanged(nameof(LastDiagnostic));
    }

    private static List<string> ParseList(string value)
    {
        return value
            .Split(',', ';')
            .Select(item => NormalizedChannel(item))
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static List<string> ParseUserList(string value)
    {
        return value
            .Split(',', ';')
            .Select(item => item.Trim().TrimStart('@'))
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string NormalizedChannel(string? value)
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

        return trimmed.Trim();
    }

    private void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public sealed class ChatRowView : INotifyPropertyChanged
{
    private static readonly Brush TransparentNameBrush = new SolidColorBrush(Color.FromRgb(0, 157, 255));
    private Brush _nameBrush = Brushes.White;
    private string _timestampText = string.Empty;

    public static ImageSource TwitchImage { get; } = new BitmapImage(new Uri("pack://application:,,,/twitch.png"));
    public static ImageSource YouTubeImage { get; } = new BitmapImage(new Uri("pack://application:,,,/youtube.png"));
    public static ImageSource ModeratorImage { get; } = new BitmapImage(new Uri("pack://application:,,,/mod.png"));
    public static ImageSource SubscriberImage { get; } = new BitmapImage(new Uri("pack://application:,,,/sub.png"));
    public static ImageSource VipImage { get; } = new BitmapImage(new Uri("pack://application:,,,/vip.png"));
    public ImageSource PlatformImage { get; set; } = TwitchImage;
    public ImageSource? BadgeImage { get; set; }
    public ImageSource? SubscriberBadgeImage { get; set; }
    public string BadgeName { get; set; } = string.Empty;
    public string SubscriberBadgeName { get; set; } = string.Empty;
    public string PlatformName { get; set; } = "Twitch";
    public string FullTimestampText { get; set; } = string.Empty;
    public string TimestampText
    {
        get => _timestampText;
        private set
        {
            if (_timestampText == value)
            {
                return;
            }

            _timestampText = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TimestampText)));
        }
    }
    public string DisplayName { get; set; } = string.Empty;
    public string BadgeText { get; set; } = string.Empty;
    public string NameSeparator { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public Brush DefaultNameBrush { get; set; } = Brushes.White;
    public Brush NameBrush
    {
        get => _nameBrush;
        private set
        {
            if (ReferenceEquals(_nameBrush, value))
            {
                return;
            }

            _nameBrush = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(NameBrush)));
        }
    }
    public Visibility BadgeVisibility { get; set; } = Visibility.Visible;
    public Visibility SubscriberBadgeVisibility { get; set; } = Visibility.Visible;

    public event PropertyChangedEventHandler? PropertyChanged;

    public void SetTransparentAppearance(bool enabled)
    {
        TimestampText = enabled ? string.Empty : FullTimestampText;
        NameBrush = enabled ? TransparentNameBrush : DefaultNameBrush;
    }
}

public partial class MainWindow : Window
{
    private const int WmHotkey = 0x0312;
    private readonly TwitchChatClient _chatClient = new();
    private readonly TwitchOAuthClient _twitchOAuthClient = new();
    private readonly YouTubeOAuthClient _youTubeOAuthClient = new();
    private readonly YouTubeChatClient _youTubeChatClient = new();
    private readonly AppSettings _settings;
    private readonly CancellationTokenSource _lifetimeCts = new();
    private TwitchCredentials? _twitchCredentials;
    private YouTubeCredentials? _youTubeCredentials;
    private CancellationTokenSource? _authorizationCts;
    private Uri? _twitchVerificationUri;
    private int _connectInProgress;
    private int _hotKeyId = 9000;
    private bool _isConnected;
    private bool _twitchConnectionActive;
    private int _youTubeConnectInProgress;
    private CancellationTokenSource? _youTubeConnectCts;
    private bool _youTubePublicChatActive;
    private bool _sharedChatFilterWarningShown;
    private int _reconnectScheduled;
    private bool _windowPositionReady;
    private bool _cursorOverResizeEdge;
    private readonly DispatcherTimer _windowPositionSaveTimer = new() { Interval = TimeSpan.FromMilliseconds(400) };
    private readonly DispatcherTimer _chatOnlyHitTestTimer = new() { Interval = TimeSpan.FromMilliseconds(40) };
    private readonly DispatcherTimer _chatOnlyGearHoldTimer = new() { Interval = TimeSpan.FromMilliseconds(5) };
    private readonly DispatcherTimer _suppressGearClickTimer = new() { Interval = TimeSpan.FromMilliseconds(300) };
    private bool _cursorOverChatOnlyGear;
    private bool _chatOnlyGearDragActive;
    private bool _suppressNextGearClick;
    private bool _chatOnlyGearPopupOffsetNudge;
    private NativeMethods.Point _chatOnlyGearDragStart;
    private double _chatOnlyGearWindowStartLeft;
    private double _chatOnlyGearWindowStartTop;
    private bool _popoutScriptRegistered;
    private bool _youTubePopoutScriptRegistered;
    private bool _popoutNavigationHandlerRegistered;
    private bool _youTubePopoutNavigationHandlerRegistered;
    private bool _twitchCaptureScriptRegistered;
    private bool _youTubeCaptureScriptRegistered;
    private bool _twitchPublicChatActive;
    private int _connectionModeChangeVersion;
    private const string TwitchCaptureScript = """
        (() => {
            if (window.__transparentChatFilterTwitchCapture) return;
            window.__transparentChatFilterTwitchCapture = true;
            const seen = new WeakSet();
            const send = () => {
                document.querySelectorAll(
                    '[data-a-target="chat-line-message"], [data-test-selector="chat-line-message"]'
                ).forEach(line => {
                    if (seen.has(line)) return;
                    const user = line.querySelector(
                        '[data-a-target="chat-message-username"], [data-test-selector="message-username"]'
                    );
                    const body = line.querySelector(
                        '[data-a-target="chat-line-message-body"], [data-test-selector="message-body"]'
                    );
                    if (!user || !body) return;
                    const displayName = (user.textContent || '').trim();
                    const message = (body.innerText || body.textContent || '').trim();
                    if (!displayName || !message) return;
                    seen.add(line);
                    const labels = (line.innerText || '').toLowerCase();
                    window.chrome.webview.postMessage({
                        platform:'Twitch', displayName, message,
                        channel:location.pathname.split('/').filter(Boolean)[1] || '',
                        isModerator:labels.includes('moderator'),
                        isVip:labels.includes('vip'), isSubscriber:labels.includes('subscriber')
                    });
                });
            };
            new MutationObserver(send).observe(document.documentElement, {
                childList:true, subtree:true, characterData:true
            });
            setInterval(send, 1000);
            send();
        })();
        """;
    private const string YouTubeCaptureScript = """
        (() => {
            if (window.__transparentChatFilterYouTubeCapture) return;
            window.__transparentChatFilterYouTubeCapture = true;
            const seen = new WeakSet();
            const send = () => {
                document.querySelectorAll(
                    'yt-live-chat-text-message-renderer, yt-live-chat-paid-message-renderer, ' +
                    'yt-live-chat-membership-item-renderer, yt-live-chat-paid-sticker-renderer'
                ).forEach(line => {
                    if (seen.has(line)) return;
                    const author = line.querySelector('#author-name');
                    const body = line.querySelector('#message');
                    const displayName = (author?.textContent || '').trim();
                    const message = (body?.innerText || body?.textContent || line.querySelector('#purchase-amount')?.textContent || '').trim();
                    if (!displayName || !message) return;
                    seen.add(line);
                    window.chrome.webview.postMessage({
                        platform:'YouTube', displayName, message,
                        channel:new URL(location.href).searchParams.get('v') || ''
                    });
                });
            };
            new MutationObserver(send).observe(document.documentElement, {
                childList:true, subtree:true, characterData:true
            });
            setInterval(send, 1000);
            send();
        })();
        """;
    private const string TwitchMessagesOnlyScript = """
        (() => {
            const styleId = 'transparent-chat-filter-messages-only';
            const css = `
                html, body { width:100%!important; height:100%!important; margin:0!important; overflow:hidden!important; background:transparent!important; }
                body * { visibility:hidden!important; }
                [data-test-selector="chat-scrollable-area__message-container"],
                [role="log"].chat-scrollable-area__message-container {
                    position:fixed!important; inset:0!important; z-index:2147483647!important;
                    display:block!important; width:100vw!important; height:100vh!important;
                    max-height:100vh!important; overflow-x:hidden!important; overflow-y:auto!important;
                    margin:0!important; padding:0!important; background:transparent!important;
                    visibility:visible!important;
                }
                [data-test-selector="chat-scrollable-area__message-container"] *,
                [role="log"].chat-scrollable-area__message-container * {
                    visibility:visible!important;
                    -webkit-text-stroke:0!important;
                }
                [data-test-selector="chat-scrollable-area__message-container"] [data-a-target="chat-line-message"],
                [role="log"].chat-scrollable-area__message-container [data-a-target="chat-line-message"] {
                    display:block!important;
                    width:max-content!important;
                    max-width:100%!important;
                    box-sizing:border-box!important;
                    margin:2px 0!important;
                    padding:3px 7px!important;
                    border-radius:4px!important;
                    background:rgba(0,0,0,0.72)!important;
                }
                [data-test-selector="chat-scrollable-area__message-container"] [data-a-target="chat-line-message"] *,
                [role="log"].chat-scrollable-area__message-container [data-a-target="chat-line-message"] * {
                    background-color:transparent!important;
                    -webkit-text-stroke:0!important;
                    text-shadow:0 1px 1px rgba(0,0,0,0.95), 0 0 3px rgba(0,0,0,0.8)!important;
                }
            `;
            const apply = () => {
                const messages = document.querySelector(
                    '[data-test-selector="chat-scrollable-area__message-container"], [role="log"].chat-scrollable-area__message-container'
                );
                if (!messages) return false;
                const root = document.head || document.documentElement;
                if (!root) return false;
                let style = document.getElementById(styleId);
                if (!style) {
                    style = document.createElement('style');
                    style.id = styleId;
                    root.appendChild(style);
                }
                style.textContent = css;
                return true;
            };
            if (apply()) return;
            const observer = new MutationObserver(() => {
                if (apply()) observer.disconnect();
            });
            observer.observe(document.documentElement, { childList:true, subtree:true });
        })();
        """;
    private const string YouTubeMessagesOnlyScript = """
        (() => {
            const styleId = 'transparent-chat-filter-youtube-messages-only';
            const css = `
                html, body, yt-live-chat-app, yt-live-chat-renderer {
                    width:100%!important; height:100%!important; min-height:100%!important;
                    margin:0!important; overflow:hidden!important; background:transparent!important;
                }
                yt-live-chat-header-renderer, yt-live-chat-ticker-renderer,
                yt-live-chat-message-input-renderer, yt-live-chat-viewer-engagement-message-renderer,
                yt-live-chat-banner-manager, #ticker, #input-panel, #separator, #panel-pages {
                    display:none!important;
                }
                yt-live-chat-renderer, #contents, yt-live-chat-item-list-renderer,
                yt-live-chat-item-list-renderer #item-scroller {
                    height:100vh!important; max-height:100vh!important; min-height:0!important;
                    background:transparent!important;
                }
                yt-live-chat-item-list-renderer #item-scroller {
                    overflow-y:auto!important; overflow-x:hidden!important;
                }
                yt-live-chat-item-list-renderer #items {
                    padding:4px!important; background:transparent!important;
                }
                yt-live-chat-text-message-renderer, yt-live-chat-paid-message-renderer,
                yt-live-chat-membership-item-renderer, yt-live-chat-paid-sticker-renderer {
                    width:max-content!important; max-width:100%!important; box-sizing:border-box!important;
                    margin:2px 0!important; padding:3px 7px!important; border-radius:4px!important;
                    background:rgba(0,0,0,0.72)!important;
                }
                yt-live-chat-text-message-renderer *,
                yt-live-chat-paid-message-renderer *,
                yt-live-chat-membership-item-renderer *,
                yt-live-chat-paid-sticker-renderer * {
                    background-color:transparent!important;
                    text-shadow:0 1px 1px rgba(0,0,0,0.95), 0 0 3px rgba(0,0,0,0.8)!important;
                }
            `;
            const apply = () => {
                if (!document.querySelector('yt-live-chat-renderer')) return false;
                const root = document.head || document.documentElement;
                if (!root) return false;
                let style = document.getElementById(styleId);
                if (!style) {
                    style = document.createElement('style');
                    style.id = styleId;
                    root.appendChild(style);
                }
                style.textContent = css;
                return true;
            };
            if (apply()) return;
            const observer = new MutationObserver(() => {
                if (apply()) observer.disconnect();
            });
            observer.observe(document.documentElement, { childList:true, subtree:true });
        })();
        """;

    public MainWindow()
    {
        _settings = AppSettings.Load();
        _settings.IsTransparent = false;
        DataContext = new OverlayViewModel(_settings);
        InitializeComponent();
        RestoreWindowPosition();
        _windowPositionSaveTimer.Tick += WindowPositionSaveTimer_Tick;
        if (_settings.YouTubeOAuthClientIdMigrated)
        {
            try
            {
                YouTubeCredentialStore.DeleteRefreshToken();
                _settings.Save();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                ViewModel.UpdateDiagnostic($"Não foi possível atualizar a autorização Google para o novo Client ID: {ex.Message}");
            }
        }

        ChatOnlyGearPopup.CustomPopupPlacementCallback = ChatOnlyGearPopup_Placement;
        LocationChanged += MainWindow_LocationChanged;
        _chatOnlyHitTestTimer.Tick += ChatOnlyHitTestTimer_Tick;
        _chatOnlyGearHoldTimer.Tick += ChatOnlyGearHoldTimer_Tick;
        _suppressGearClickTimer.Tick += (_, _) =>
        {
            _suppressNextGearClick = false;
            _suppressGearClickTimer.Stop();
        };
        FilterModeComboBox.SelectedIndex = (int)_settings.FilterMode;
        ConnectionModeComboBox.SelectedIndex = (int)_settings.ConnectionMode;
        UpdateConnectionModeUi();
        UpdateYouTubePublicChatButtonVisibility();
        try
        {
            _twitchCredentials = TwitchCredentialStore.LoadCredentials(_settings.TwitchUsername);
            if (!string.IsNullOrWhiteSpace(_twitchCredentials?.Username))
            {
                ViewModel.TwitchUsername = _twitchCredentials.Username;
            }

            if (_twitchCredentials is not null)
            {
                TwitchAuthorizationStatus.Text = string.IsNullOrWhiteSpace(_twitchCredentials.Username)
                    ? "Credenciais Twitch salvas neste dispositivo."
                    : $"Credenciais Twitch salvas para @{_twitchCredentials.Username}.";
            }
        }
        catch (Exception ex) when (ex is IOException or FormatException or System.ComponentModel.Win32Exception)
        {
            ViewModel.UpdateDiagnostic($"Não foi possível ler a autorização Twitch armazenada: {ex.Message}");
        }

        UpdateConnectionModeUi();
        UpdateYouTubePublicChatButtonVisibility();
        this.Closed += MainWindow_Closed;
        this.Loaded += MainWindow_Loaded;
        this.StateChanged += MainWindow_StateChanged;
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
                ViewModel.SaveSettings();
            });
        };

        _chatClient.MessageReceived += (_, message) =>
        {
            DispatchToUi(() =>
            {
                var viewModel = ViewModel;
                if (!ShouldDisplayMessage(message, viewModel.Settings))
                {
                    WarnWhenJoinedMessageIsFiltered(message, viewModel.Settings);
                    return;
                }

                var row = CreateChatRow(message, viewModel.Settings);
                if (viewModel.Messages.Count >= viewModel.Settings.MaxVisibleMessages)
                {
                    viewModel.Messages.RemoveAt(0);
                }

                viewModel.Messages.Add(row);
                ChatMessagesScrollViewer.ScrollToEnd();
            });
        };

        _youTubeChatClient.MessageReceived += (_, message) =>
        {
            DispatchToUi(() =>
            {
                var viewModel = ViewModel;
                if (!ShouldDisplayMessage(message, viewModel.Settings))
                {
                    return;
                }

                if (viewModel.Messages.Count >= viewModel.Settings.MaxVisibleMessages)
                {
                    viewModel.Messages.RemoveAt(0);
                }

                viewModel.Messages.Add(CreateChatRow(message, viewModel.Settings));
                ChatMessagesScrollViewer.ScrollToEnd();
            });
        };

        _youTubeChatClient.StatusChanged += (_, status) =>
        {
            DispatchToUi(() =>
            {
                YouTubeConnectionStatus.Text = status;
                UpdateOverallConnectionState();
                RefreshChatPanels();
                if (!_twitchConnectionActive)
                {
                    ViewModel.ConnectionStatus = status;
                }
            });
        };

        _youTubeChatClient.ErrorReported += (_, error) =>
        {
            DispatchToUi(() =>
            {
                YouTubeConnectionStatus.Text = "Não foi possível manter a conexão.";
                ViewModel.UpdateDiagnostic(error);
                MessageBox.Show(this, error, "Chat do YouTube", MessageBoxButton.OK, MessageBoxImage.Warning);
            });
        };

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

        if (_settings.StartMinimized)
        {
            WindowState = WindowState.Minimized;
        }

        var viewModel = ViewModel;
        var defaultFilterMode = (int)_settings.FilterMode;
        if (FilterModeComboBox.SelectedIndex != defaultFilterMode)
        {
            FilterModeComboBox.SelectedIndex = defaultFilterMode;
        }
    }

    public OverlayViewModel ViewModel => (OverlayViewModel)DataContext;

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        ApplyAppearance();
        RootBorder.Opacity = 0;
        RootBorder.RenderTransform = new ScaleTransform(0.985, 0.985);
        RootBorder.RenderTransformOrigin = new Point(0.5, 0.5);
        RootBorder.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(260))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        });
        ((ScaleTransform)RootBorder.RenderTransform).BeginAnimation(ScaleTransform.ScaleXProperty,
            new DoubleAnimation(0.985, 1, TimeSpan.FromMilliseconds(300)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        ((ScaleTransform)RootBorder.RenderTransform).BeginAnimation(ScaleTransform.ScaleYProperty,
            new DoubleAnimation(0.985, 1, TimeSpan.FromMilliseconds(300)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });

        if (string.IsNullOrWhiteSpace(ViewModel.MainChannel))
        {
            WindowState = WindowState.Normal;
            ViewModel.ConnectionStatus = "Selecione uma live nas configurações";
            ShowPreferencesPage(ChatPreferencesPage, ChatPreferencesButton);
            ToggleSettingsPopup(true);
            return;
        }

        if (_settings.StartMinimized)
        {
            return;
        }

        await ConnectAsync(showError: false, allowAuthorization: false);
    }

    private void MainWindow_Closed(object? sender, EventArgs e)
    {
        _windowPositionSaveTimer.Stop();
        SaveWindowPosition();
        _lifetimeCts.Cancel();
        _authorizationCts?.Cancel();
        _ = _youTubeChatClient.DisconnectAsync();
        _chatOnlyHitTestTimer.Stop();
        _chatOnlyGearHoldTimer.Stop();
        _suppressGearClickTimer.Stop();
        _chatClient.Dispose();
        UnregisterHotKey();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        if (PresentationSource.FromVisual(this) is HwndSource source)
        {
            source.AddHook(WindowMessageHook);
        }

        RegisterHotKey();
        UpdateClickThroughStyle();
    }

    private IntPtr WindowMessageHook(
        IntPtr hwnd,
        int message,
        IntPtr wParam,
        IntPtr lParam,
        ref bool handled)
    {
        if (message != NativeMethods.WmNcHitTest ||
            ResizeMode != ResizeMode.CanResize ||
            WindowState != WindowState.Normal)
        {
            return IntPtr.Zero;
        }

        var packedPoint = lParam.ToInt64();
        var screenX = unchecked((short)(packedPoint & 0xFFFF));
        var screenY = unchecked((short)((packedPoint >> 16) & 0xFFFF));
        var point = PointFromScreen(new Point(screenX, screenY));
        const double resizeBorder = 8;
        var left = point.X <= resizeBorder;
        var right = point.X >= ActualWidth - resizeBorder;
        var top = point.Y <= resizeBorder;
        var bottom = point.Y >= ActualHeight - resizeBorder;
        var hitTest = (left, right, top, bottom) switch
        {
            (true, _, true, _) => NativeMethods.HtTopLeft,
            (_, true, true, _) => NativeMethods.HtTopRight,
            (true, _, _, true) => NativeMethods.HtBottomLeft,
            (_, true, _, true) => NativeMethods.HtBottomRight,
            (true, _, _, _) => NativeMethods.HtLeft,
            (_, true, _, _) => NativeMethods.HtRight,
            (_, _, true, _) => NativeMethods.HtTop,
            (_, _, _, true) => NativeMethods.HtBottom,
            _ => 0
        };

        if (hitTest == 0)
        {
            return IntPtr.Zero;
        }

        handled = true;
        return new IntPtr(hitTest);
    }

    private void RegisterHotKey()
    {
        var helper = new WindowInteropHelper(this);
        var handle = helper.Handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        NativeMethods.RegisterHotKey(handle, _hotKeyId, NativeMethods.ModAlt | NativeMethods.ModControl, (int)KeyInterop.VirtualKeyFromKey(Key.T));
        NativeMethods.RegisterHotKey(handle, _hotKeyId + 1, NativeMethods.ModAlt | NativeMethods.ModControl | NativeMethods.ModShift, (int)KeyInterop.VirtualKeyFromKey(Key.T));
        HwndSource.FromHwnd(handle)?.AddHook(WndProc);
    }

    private void UnregisterHotKey()
    {
        var helper = new WindowInteropHelper(this);
        var handle = helper.Handle;
        if (handle != IntPtr.Zero)
        {
            NativeMethods.UnregisterHotKey(handle, _hotKeyId);
            NativeMethods.UnregisterHotKey(handle, _hotKeyId + 1);
        }
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmHotkey && wParam.ToInt32() == _hotKeyId)
        {
            Visibility = Visibility == Visibility.Visible ? Visibility.Hidden : Visibility.Visible;
            handled = true;
        }
        else if (msg == WmHotkey && wParam.ToInt32() == _hotKeyId + 1)
        {
            SetChatOnlyMode(!_settings.IsTransparent);
            handled = true;
        }

        return IntPtr.Zero;
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

    private void ToggleOverlayButton_Click(object sender, RoutedEventArgs e)
    {
        Visibility = Visibility == Visibility.Visible ? Visibility.Hidden : Visibility.Visible;
    }

    private void ToggleSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        ToggleSettingsPopup(!SettingsPopup.IsOpen);
    }

    private void ToggleSettingsPopup(bool isOpen)
    {
        ViewModel.SetSettingsVisible(isOpen);
        SettingsPanel.Visibility = isOpen ? Visibility.Visible : Visibility.Collapsed;
        if (isOpen)
        {
            Activate();
            NativeMethods.SetForegroundWindow(new WindowInteropHelper(this).Handle);
        }

        SettingsPopup.IsOpen = isOpen;
        ViewModel.SaveSettings();
    }

    private void SettingsPopup_Closed(object? sender, EventArgs e)
    {
        ViewModel.SetSettingsVisible(false);
        ViewModel.SaveSettings();
    }

    private void SettingsPopup_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (!IsActive)
        {
            Activate();
            NativeMethods.SetForegroundWindow(new WindowInteropHelper(this).Handle);
        }
    }

    private void ChatPreferencesButton_Click(object sender, RoutedEventArgs e) => ShowPreferencesPage(ChatPreferencesPage, ChatPreferencesButton);

    private void YouTubePreferencesButton_Click(object sender, RoutedEventArgs e) => ShowPreferencesPage(YouTubePreferencesPage, YouTubePreferencesButton);

    private void YouTubeConnectionsPreferencesButton_Click(object sender, RoutedEventArgs e) =>
        ShowPreferencesPage(YouTubeConnectionsPreferencesPage, YouTubeConnectionsPreferencesButton);

    private async void OpenYouTubePublicChatButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var videoId = NormalizeYouTubeVideoId(ViewModel.YouTubeVideoId);
            if (string.IsNullOrWhiteSpace(videoId))
            {
                throw new InvalidOperationException("Cole o link da transmissão ao vivo do YouTube.");
            }

            await _youTubeChatClient.DisconnectAsync();
            _youTubePublicChatActive = true;
            await ShowYouTubePublicChatAsync(videoId);
            YouTubePublicChatView.Visibility = Visibility.Visible;
            RefreshChatPanels();
            UpdateChatOnlyControlsVisibility();
            UpdateOverallConnectionState();
            if (!_twitchConnectionActive)
            {
                ViewModel.ConnectionStatus = "Chat público do YouTube";
            }

            YouTubeConnectionStatus.Text = "Chat público aberto · sem login";
            YouTubePublicChatStatus.Text = "Chat público aberto. Esta opção não autoriza a conta Google nem inicia a conexão autenticada do YouTube.";
        }
        catch (Exception ex)
        {
            if (_youTubePublicChatActive)
            {
                _youTubePublicChatActive = false;
                YouTubePublicChatView.Visibility = Visibility.Collapsed;
                UpdateChatOnlyControlsVisibility();
                RefreshChatPanels();
                UpdateOverallConnectionState();
            }

            YouTubeConnectionStatus.Text = "Não foi possível abrir o chat público.";
            YouTubePublicChatStatus.Text = "Não foi possível abrir o chat público.";
            ViewModel.UpdateDiagnostic($"YouTube: {ex.Message}");
            MessageBox.Show(this, ex.Message, "Chat público do YouTube", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void ConnectYouTubeButton_Click(object sender, RoutedEventArgs e)
    {
        if (Interlocked.Exchange(ref _youTubeConnectInProgress, 1) != 0)
        {
            return;
        }

        var connectionCts = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCts.Token);
        _youTubeConnectCts = connectionCts;
        try
        {
            CancelYouTubeConnectionButton.Visibility = Visibility.Visible;
            var videoId = NormalizeYouTubeVideoId(ViewModel.YouTubeVideoId);
            if (string.IsNullOrWhiteSpace(videoId))
            {
                throw new InvalidOperationException("Informe o ID ou o link do vídeo ao vivo do YouTube.");
            }

            _youTubePublicChatActive = false;
            YouTubePublicChatView.CoreWebView2?.Stop();
            YouTubePublicChatView.Visibility = Visibility.Collapsed;
            RefreshChatPanels();
            UpdateChatOnlyControlsVisibility();
            YouTubeConnectionStatus.Text = "Preparando a autorização do Google...";
            var refreshToken = YouTubeCredentialStore.LoadRefreshToken();
            if (string.IsNullOrWhiteSpace(refreshToken))
            {
                YouTubeConnectionStatus.Text = "Aguardando autorização no navegador...";
                _youTubeCredentials = await _youTubeOAuthClient.AuthorizeAsync(connectionCts.Token);
                YouTubeCredentialStore.SaveRefreshToken(_youTubeCredentials.RefreshToken);
            }
            else
            {
                _youTubeCredentials = new YouTubeCredentials(string.Empty, refreshToken, DateTimeOffset.MinValue);
                await GetYouTubeAccessTokenAsync(connectionCts.Token);
            }

            await _youTubeChatClient.ConnectAsync(
                videoId,
                GetYouTubeAccessTokenAsync,
                connectionCts.Token);
        }
        catch (OperationCanceledException) when (connectionCts.IsCancellationRequested)
        {
            YouTubeConnectionStatus.Text = "Conexão cancelada.";
        }
        catch (Exception ex)
        {
            YouTubeConnectionStatus.Text = "Não foi possível conectar.";
            ViewModel.UpdateDiagnostic($"YouTube: {ex.Message}");
            MessageBox.Show(
                this,
                ex.Message.Contains("redirect_uri_mismatch", StringComparison.OrdinalIgnoreCase)
                    ? "O Google rejeitou o endereço de retorno local.\n\nEm Conexões, informe o Client ID de um cliente OAuth do tipo Aplicativo para computador. Um Client ID do tipo Aplicativo da Web não aceita a porta local dinâmica usada pelo app."
                    : ex.Message,
                "Não foi possível conectar ao YouTube",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        finally
        {
            if (ReferenceEquals(_youTubeConnectCts, connectionCts))
            {
                _youTubeConnectCts = null;
            }

            connectionCts.Dispose();
            CancelYouTubeConnectionButton.Visibility = Visibility.Collapsed;
            Interlocked.Exchange(ref _youTubeConnectInProgress, 0);
        }
    }

    private void CancelYouTubeConnectionButton_Click(object sender, RoutedEventArgs e)
    {
        _youTubeConnectCts?.Cancel();
    }

    private async void DisconnectYouTubeButton_Click(object sender, RoutedEventArgs e)
    {
        _youTubeConnectCts?.Cancel();
        await _youTubeChatClient.DisconnectAsync();
        if (_youTubePublicChatActive)
        {
            _youTubePublicChatActive = false;
            YouTubePublicChatView.CoreWebView2?.Stop();
            YouTubePublicChatView.Visibility = Visibility.Collapsed;
            RefreshChatPanels();
            UpdateChatOnlyControlsVisibility();
        }

        UpdateOverallConnectionState();
        RefreshChatPanels();
        YouTubePublicChatStatus.Text = "Chat público desconectado";
        YouTubeConnectionStatus.Text = "YouTube desconectado";
        if (!_twitchConnectionActive)
        {
            ViewModel.ConnectionStatus = "Desconectado";
        }
    }

    private async Task<string> GetYouTubeAccessTokenAsync(CancellationToken cancellationToken)
    {
        if (_youTubeCredentials is null || _youTubeCredentials.ExpiresAtUtc <= DateTimeOffset.UtcNow.AddMinutes(1))
        {
            var refreshToken = _youTubeCredentials?.RefreshToken ?? YouTubeCredentialStore.LoadRefreshToken();
            if (string.IsNullOrWhiteSpace(refreshToken))
            {
                throw new InvalidOperationException("Autorize sua conta Google para conectar ao chat do YouTube.");
            }

            var refreshed = await _youTubeOAuthClient.RefreshAsync(
                new YouTubeCredentials(string.Empty, refreshToken, DateTimeOffset.MinValue),
                cancellationToken);
            _youTubeCredentials = refreshed;
        }

        return _youTubeCredentials.AccessToken;
    }

    private static string NormalizeYouTubeVideoId(string input)
    {
        var value = input.Trim();
        if (Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
            (uri.Host.Equals("youtube.com", StringComparison.OrdinalIgnoreCase) ||
             uri.Host.EndsWith(".youtube.com", StringComparison.OrdinalIgnoreCase) ||
             uri.Host.Equals("youtu.be", StringComparison.OrdinalIgnoreCase)))
        {
            if (uri.Host.Equals("youtu.be", StringComparison.OrdinalIgnoreCase))
            {
                return uri.AbsolutePath.Trim('/');
            }

            var videoId = uri.Query.TrimStart('?')
                .Split('&', StringSplitOptions.RemoveEmptyEntries)
                .Select(pair => pair.Split('=', 2))
                .Where(pair => pair.Length == 2 && pair[0] == "v")
                .Select(pair => Uri.UnescapeDataString(pair[1]))
                .FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(videoId))
            {
                return videoId;
            }

            var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
            var liveIndex = Array.FindIndex(segments, segment => segment.Equals("live", StringComparison.OrdinalIgnoreCase));
            if (liveIndex >= 0 && liveIndex + 1 < segments.Length)
            {
                return segments[liveIndex + 1];
            }
        }

        return value;
    }

    private void ConnectionsPreferencesButton_Click(object sender, RoutedEventArgs e) => ShowPreferencesPage(ConnectionsPreferencesPage, ConnectionsPreferencesButton);

    private void WidgetsPreferencesButton_Click(object sender, RoutedEventArgs e) => ShowPreferencesPage(WidgetsPreferencesPage, WidgetsPreferencesButton);

    private void AboutPreferencesButton_Click(object sender, RoutedEventArgs e) => ShowPreferencesPage(AboutPreferencesPage, AboutPreferencesButton);

    private void ShowPreferencesPage(FrameworkElement page, Button activeButton)
    {
        ChatPreferencesPage.Visibility = Visibility.Collapsed;
        YouTubePreferencesPage.Visibility = Visibility.Collapsed;
        YouTubeConnectionsPreferencesPage.Visibility = Visibility.Collapsed;
        ConnectionsPreferencesPage.Visibility = Visibility.Collapsed;
        WidgetsPreferencesPage.Visibility = Visibility.Collapsed;
        AboutPreferencesPage.Visibility = Visibility.Collapsed;
        page.Visibility = Visibility.Visible;

        foreach (var button in new[]
                 {
                     ChatPreferencesButton,
                     YouTubePreferencesButton,
                     YouTubeConnectionsPreferencesButton,
                     ConnectionsPreferencesButton,
                     WidgetsPreferencesButton,
                     AboutPreferencesButton
                 })
        {
            button.Background = new SolidColorBrush(Color.FromRgb(32, 45, 64));
            button.BorderBrush = new SolidColorBrush(Color.FromRgb(52, 68, 91));
            button.Foreground = new SolidColorBrush(Color.FromRgb(234, 242, 255));
        }

        activeButton.Background = new SolidColorBrush(Color.FromRgb(48, 37, 68));
        activeButton.BorderBrush = new SolidColorBrush(Color.FromRgb(128, 84, 197));
        activeButton.Foreground = new SolidColorBrush(Color.FromRgb(241, 232, 255));
    }

    private async void DisconnectButton_Click(object sender, RoutedEventArgs e)
    {
        _youTubePublicChatActive = false;
        _twitchPublicChatActive = false;
        _chatClient.Disconnect();
        await _youTubeChatClient.DisconnectAsync();
        TwitchPopoutView.CoreWebView2?.Stop();
        TwitchPopoutView.Visibility = Visibility.Collapsed;
        YouTubePublicChatView.CoreWebView2?.Stop();
        YouTubePublicChatView.Visibility = Visibility.Collapsed;
        RefreshChatPanels();
        UpdateChatOnlyControlsVisibility();
        SetConnected(false);
        ViewModel.ConnectionStatus = "Desconectado";
        ViewModel.SaveSettings();
    }

    private void SavePreferences_Changed(object sender, RoutedEventArgs e)
    {
        ApplyAppearance();
        ViewModel.SaveSettings();
    }

    private void FontSizeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (IsInitialized)
        {
            Dispatcher.BeginInvoke(new Action(ViewModel.SaveSettings), DispatcherPriority.DataBind);
        }
    }

    private void SaveTextPreference_Changed(object sender, TextChangedEventArgs e)
    {
        if (IsInitialized)
        {
            ViewModel.SaveSettings();
        }
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
        ViewModel.SaveSettings();
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
        Toolbar.BeginAnimation(OpacityProperty, new DoubleAnimation(opacity, TimeSpan.FromMilliseconds(180))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        });
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left)
        {
            return;
        }

        if (e.ClickCount == 2)
        {
            ToggleMaximizeRestore();
            return;
        }

        DragMove();
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
        MaximizeButton.Content = WindowState == WindowState.Maximized ? "❐" : "□";
        MaximizeButton.ToolTip = WindowState == WindowState.Maximized ? "Restaurar" : "Maximizar";
        ScheduleWindowPositionSave();
    }

    private void MainWindow_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        ScheduleWindowPositionSave();
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
        _settings.Save();
    }

    private void SetConnected(bool connected)
    {
        _twitchConnectionActive = connected;
        ViewModel.IsTwitchConnected = connected;
        UpdateYouTubePublicChatButtonVisibility();
        UpdateOverallConnectionState();
    }

    private void UpdateYouTubePublicChatButtonVisibility()
    {
        if (!IsInitialized)
        {
            return;
        }

        YouTubePublicChatButton.Visibility =
            _settings.ConnectionMode == ChatConnectionMode.AuthenticatedIrc && _chatClient.IsConnected
                ? Visibility.Collapsed
                : Visibility.Visible;
    }

    private void UpdateOverallConnectionState()
    {
        _isConnected = _twitchConnectionActive || _youTubeChatClient.IsConnected || _youTubePublicChatActive;
        ViewModel.IsConnected = _isConnected;
    }

    private void DisconnectTwitch()
    {
        _chatClient.Disconnect();
        if (_settings.ConnectionMode == ChatConnectionMode.PublicPopout &&
            _twitchPublicChatActive)
        {
            TwitchPopoutView.CoreWebView2?.Stop();
            _twitchPublicChatActive = false;
        }

        SetConnected(false);
        RefreshChatPanels();
        UpdateChatOnlyControlsVisibility();
        ViewModel.ConnectionStatus = _youTubeChatClient.IsConnected
            ? "YouTube conectado"
            : _youTubePublicChatActive ? "Chat público do YouTube"
            : _twitchPublicChatActive ? "Chat público da Twitch" : "Desconectado";
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

    private void ExitChatOnlyButton_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _suppressNextGearClick = false;
        _suppressGearClickTimer.Stop();
        if (!NativeMethods.GetCursorPos(out _chatOnlyGearDragStart))
        {
            ViewModel.UpdateDiagnostic("Não foi possível iniciar o arrasto do chat transparente.");
            return;
        }

        _chatOnlyGearWindowStartLeft = Left;
        _chatOnlyGearWindowStartTop = Top;
        _chatOnlyGearHoldTimer.Start();
    }

    private void ExitChatOnlyButton_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _chatOnlyGearHoldTimer.Stop();
        FinishChatOnlyGearDrag();
    }

    private void ChatOnlyGearHoldTimer_Tick(object? sender, EventArgs e)
    {
        if (!_settings.IsTransparent || Mouse.LeftButton != MouseButtonState.Pressed)
        {
            _chatOnlyGearHoldTimer.Stop();
            FinishChatOnlyGearDrag();
            return;
        }

        if (!NativeMethods.GetCursorPos(out var cursorPosition))
        {
            _chatOnlyGearHoldTimer.Stop();
            ViewModel.UpdateDiagnostic("Não foi possível acompanhar o movimento do chat transparente.");
            return;
        }

        var deltaX = Math.Abs(cursorPosition.X - _chatOnlyGearDragStart.X);
        var deltaY = Math.Abs(cursorPosition.Y - _chatOnlyGearDragStart.Y);
        if (deltaX < SystemParameters.MinimumHorizontalDragDistance &&
            deltaY < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        _chatOnlyGearDragActive = true;
        _suppressNextGearClick = true;
        var dpi = VisualTreeHelper.GetDpi(this);
        Left = _chatOnlyGearWindowStartLeft +
               (cursorPosition.X - _chatOnlyGearDragStart.X) / dpi.DpiScaleX;
        Top = _chatOnlyGearWindowStartTop +
              (cursorPosition.Y - _chatOnlyGearDragStart.Y) / dpi.DpiScaleY;
    }

    private void FinishChatOnlyGearDrag()
    {
        if (!_chatOnlyGearDragActive)
        {
            return;
        }

        _chatOnlyGearDragActive = false;
        _suppressNextGearClick = true;
        _suppressGearClickTimer.Stop();
        _suppressGearClickTimer.Start();
    }

    private void ChatOnlyHitTestTimer_Tick(object? sender, EventArgs e)
    {
        var gearButton = ChatOnlyGearPopup.IsOpen
            ? (FrameworkElement)ChatOnlyGearPopupButton
            : ExitChatOnlyButton;
        if (!IsLoaded || !NativeMethods.GetCursorPos(out var cursorPosition))
        {
            return;
        }

        var cursorScreenPoint = new Point(cursorPosition.X, cursorPosition.Y);
        var cursorInWindow = PointFromScreen(cursorScreenPoint);
        const double resizeBorder = 8;
        var overResizeEdge = cursorInWindow.X >= 0 && cursorInWindow.Y >= 0 &&
                             cursorInWindow.X <= ActualWidth && cursorInWindow.Y <= ActualHeight &&
                             (cursorInWindow.X <= resizeBorder || cursorInWindow.X >= ActualWidth - resizeBorder ||
                              cursorInWindow.Y <= resizeBorder || cursorInWindow.Y >= ActualHeight - resizeBorder);
        var overGear = gearButton.IsVisible &&
                       new Rect(0, 0, gearButton.ActualWidth, gearButton.ActualHeight)
                           .Contains(gearButton.PointFromScreen(cursorScreenPoint));
        if (_cursorOverChatOnlyGear != overGear || _cursorOverResizeEdge != overResizeEdge)
        {
            _cursorOverChatOnlyGear = overGear;
            _cursorOverResizeEdge = overResizeEdge;
            UpdateClickThroughStyle();
        }
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
        try
        {
            _settings.Save();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ViewModel.UpdateDiagnostic($"Não foi possível salvar a posição da janela: {ex.Message}");
        }
    }

    private async void ApplyChannelButton_Click(object sender, RoutedEventArgs e)
    {
        await ConnectAsync();
    }

    private async void FilterModeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var comboBox = (ComboBox)sender;
        if (comboBox.SelectedIndex < 0)
        {
            return;
        }

        ViewModel.FilterMode = (ChatFilterMode)comboBox.SelectedIndex;
        _sharedChatFilterWarningShown = false;
        ViewModel.Messages.Clear();
        ViewModel.SaveSettings();

        if (_twitchConnectionActive && _settings.ConnectionMode == ChatConnectionMode.AuthenticatedIrc)
        {
            ViewModel.ConnectionStatus = "Atualizando canais do filtro...";
            await ConnectAsync(allowAuthorization: false);
        }
    }

    private void WarnWhenJoinedMessageIsFiltered(ChatMessage message, AppSettings settings)
    {
        if (_sharedChatFilterWarningShown ||
            message.Platform != ChatPlatform.Twitch ||
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
        if (!IsInitialized || ConnectionModeComboBox.SelectedIndex < 0)
        {
            return;
        }

        var selectedMode = (ChatConnectionMode)ConnectionModeComboBox.SelectedIndex;
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
        _authorizationCts?.Cancel();
        UpdateConnectionModeUi();
        _chatClient.Disconnect();
        SetConnected(false);
        TwitchPopoutView.CoreWebView2?.Stop();
        _twitchPublicChatActive = false;
        RefreshChatPanels();
        UpdateChatOnlyControlsVisibility();
        ViewModel.ConnectionStatus = resumeTwitchConnection
            ? "Alterando método de conexão..."
            : "Método alterado · clique em Conectar";
        ViewModel.SaveSettings();

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
        var filtersAvailable = ircMode && _twitchCredentials is not null;
        ConnectionModeDescription.Text = ircMode
            ? "Usa conexão IRC autenticada para obter mensagens estruturadas e aplicar os filtros de canal, usuário e comandos."
            : "Mostra as mensagens do chat público da Twitch no feed, sem autorização. Os filtros Meu Chat e Chat Unido também se aplicam.";
        ChatFilterModeInfo.Visibility = Visibility.Collapsed;
        AuthenticatedFilterControls.Visibility = filtersAvailable ? Visibility.Visible : Visibility.Collapsed;
        FilterAuthenticationPrompt.Visibility = filtersAvailable ? Visibility.Collapsed : Visibility.Visible;
        TwitchAuthorizationPanel.Visibility = ircMode ? Visibility.Visible : Visibility.Collapsed;
        TwitchAuthorizationHelp.Visibility = ircMode ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OpenFilterAuthentication_Click(object sender, RoutedEventArgs e)
    {
        ConnectionModeComboBox.SelectedIndex = (int)ChatConnectionMode.AuthenticatedIrc;
        ToggleSettingsPopup(true);
        ShowPreferencesPage(ConnectionsPreferencesPage, ConnectionsPreferencesButton);
    }

    private void UpdateChatOnlyControlsVisibility()
    {
        var chatOnly = _settings.IsTransparent;
        var publicChatVisible = TwitchPopoutView.Visibility == Visibility.Visible ||
                                YouTubePublicChatView.Visibility == Visibility.Visible;
        ExitChatOnlyButton.Visibility = chatOnly && !publicChatVisible
            ? Visibility.Visible
            : Visibility.Collapsed;
        ChatOnlyGearPopup.IsOpen = chatOnly && publicChatVisible;
    }

    private void RefreshChatPanels()
    {
        ChatMessagesScrollViewer.Visibility = Visibility.Visible;
        TwitchPopoutView.Visibility = _twitchPublicChatActive
            ? Visibility.Visible
            : Visibility.Collapsed;
        YouTubePublicChatView.Visibility = _youTubePublicChatActive
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void PublicChat_WebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            using var document = JsonDocument.Parse(e.WebMessageAsJson);
            var root = document.RootElement;
            var platform = root.TryGetProperty("platform", out var platformValue) &&
                           string.Equals(platformValue.GetString(), "YouTube", StringComparison.Ordinal)
                ? ChatPlatform.YouTube
                : ChatPlatform.Twitch;
            if ((platform == ChatPlatform.Twitch && !_twitchPublicChatActive) ||
                (platform == ChatPlatform.YouTube && !_youTubePublicChatActive))
            {
                return;
            }

            var displayName = root.TryGetProperty("displayName", out var nameValue)
                ? nameValue.GetString()?.Trim() ?? string.Empty
                : string.Empty;
            var text = root.TryGetProperty("message", out var messageValue)
                ? messageValue.GetString()?.Trim() ?? string.Empty
                : string.Empty;
            if (string.IsNullOrWhiteSpace(displayName) || string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            var channel = root.TryGetProperty("channel", out var channelValue)
                ? channelValue.GetString()?.Trim() ?? string.Empty
                : string.Empty;
            var message = new ChatMessage
            {
                Platform = platform,
                DisplayName = displayName,
                LoginName = displayName,
                Message = text,
                Channel = channel,
                OriginChannel = channel,
                IsModerator = root.TryGetProperty("isModerator", out var moderatorValue) && moderatorValue.GetBoolean(),
                IsVip = root.TryGetProperty("isVip", out var vipValue) && vipValue.GetBoolean(),
                IsSubscriber = root.TryGetProperty("isSubscriber", out var subscriberValue) && subscriberValue.GetBoolean(),
                TimestampUtc = DateTimeOffset.UtcNow
            };

            DispatchToUi(() => AppendMessageToFeed(message));
        }
        catch (JsonException ex)
        {
            ViewModel.UpdateDiagnostic($"Não foi possível ler uma mensagem do chat público: {ex.Message}");
        }
    }

    private void AppendMessageToFeed(ChatMessage message)
    {
        var viewModel = ViewModel;
        if (!ShouldDisplayMessage(message, viewModel.Settings))
        {
            WarnWhenJoinedMessageIsFiltered(message, viewModel.Settings);
            return;
        }

        if (viewModel.Messages.Count >= viewModel.Settings.MaxVisibleMessages)
        {
            viewModel.Messages.RemoveAt(0);
        }

        viewModel.Messages.Add(CreateChatRow(message, viewModel.Settings));
        ChatMessagesScrollViewer.ScrollToEnd();
    }

    private async Task ShowTwitchPopoutAsync(string channel)
    {
        try
        {
            await TwitchPopoutView.EnsureCoreWebView2Async();
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            throw new InvalidOperationException(
                "Não foi possível iniciar o navegador interno WebView2. Instale ou atualize o Microsoft Edge WebView2 Runtime e tente novamente.",
                ex);
        }

        var core = TwitchPopoutView.CoreWebView2
            ?? throw new InvalidOperationException("O navegador interno não foi inicializado.");
        core.Settings.AreDefaultContextMenusEnabled = false;
        core.Settings.IsStatusBarEnabled = false;
        core.Settings.IsZoomControlEnabled = false;
        core.Settings.IsBuiltInErrorPageEnabled = false;
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

        RegisterPopoutNavigationHandler();
        _twitchPublicChatActive = true;
        TwitchPopoutView.Visibility = Visibility.Visible;
        core.Navigate($"https://www.twitch.tv/popout/{Uri.EscapeDataString(channel)}/chat?popout=");
    }

    private async Task ShowYouTubePublicChatAsync(string videoId)
    {
        try
        {
            await YouTubePublicChatView.EnsureCoreWebView2Async();
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            throw new InvalidOperationException(
                "Não foi possível iniciar o navegador interno WebView2. Instale ou atualize o Microsoft Edge WebView2 Runtime e tente novamente.",
                ex);
        }

        var core = YouTubePublicChatView.CoreWebView2
            ?? throw new InvalidOperationException("O navegador interno não foi inicializado.");
        core.Settings.AreDefaultContextMenusEnabled = false;
        core.Settings.IsStatusBarEnabled = false;
        core.Settings.IsZoomControlEnabled = false;
        core.Settings.IsBuiltInErrorPageEnabled = false;
        if (!_youTubePopoutScriptRegistered)
        {
            await core.AddScriptToExecuteOnDocumentCreatedAsync(YouTubeMessagesOnlyScript);
            _youTubePopoutScriptRegistered = true;
        }
        if (!_youTubeCaptureScriptRegistered)
        {
            await core.AddScriptToExecuteOnDocumentCreatedAsync(YouTubeCaptureScript);
            core.WebMessageReceived += PublicChat_WebMessageReceived;
            _youTubeCaptureScriptRegistered = true;
        }

        RegisterYouTubePopoutNavigationHandler();
        YouTubePublicChatView.Visibility = Visibility.Visible;
        core.Navigate($"https://www.youtube.com/live_chat?v={Uri.EscapeDataString(videoId)}&is_popout=1");
    }

    private void RegisterPopoutNavigationHandler()
    {
        if (_popoutNavigationHandlerRegistered)
        {
            return;
        }

        TwitchPopoutView.NavigationCompleted += TwitchPopoutView_NavigationCompleted;
        _popoutNavigationHandlerRegistered = true;
    }

    private void RegisterYouTubePopoutNavigationHandler()
    {
        if (_youTubePopoutNavigationHandlerRegistered)
        {
            return;
        }

        YouTubePublicChatView.NavigationCompleted += YouTubePublicChatView_NavigationCompleted;
        _youTubePopoutNavigationHandlerRegistered = true;
    }

    private void TwitchPopoutView_NavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        if (TwitchPopoutView.Visibility != Visibility.Visible)
        {
            return;
        }

        if (e.IsSuccess)
        {
            if (_settings.ConnectionMode == ChatConnectionMode.PublicPopout)
            {
                _ = ApplyTwitchMessagesOnlyAsync();
            }

            return;
        }

        SetConnected(false);
        _twitchPublicChatActive = false;
        var reason = e.WebErrorStatus == CoreWebView2WebErrorStatus.CertificateIsInvalid
            ? "O certificado da página não pôde ser validado."
            : $"A página pública do chat não carregou ({e.WebErrorStatus}).";
        TwitchPopoutView.Visibility = Visibility.Collapsed;
        RefreshChatPanels();
        UpdateChatOnlyControlsVisibility();
        ViewModel.ConnectionStatus = "Falha ao carregar chat público";

        ViewModel.UpdateDiagnostic(reason);
    }

    private void YouTubePublicChatView_NavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        if (!_youTubePublicChatActive ||
            YouTubePublicChatView.Visibility != Visibility.Visible)
        {
            return;
        }

        if (e.IsSuccess)
        {
            _ = ApplyYouTubeMessagesOnlyAsync();
            return;
        }

        _youTubePublicChatActive = false;
        YouTubePublicChatView.Visibility = Visibility.Collapsed;
        RefreshChatPanels();
        UpdateChatOnlyControlsVisibility();
        UpdateOverallConnectionState();
        YouTubeConnectionStatus.Text = "Não foi possível abrir o chat público.";
        ViewModel.ConnectionStatus = "Falha ao carregar chat público do YouTube";
        ViewModel.UpdateDiagnostic($"O chat público do YouTube não carregou ({e.WebErrorStatus}).");
    }

    private async Task ApplyTwitchMessagesOnlyAsync()
    {
        var core = TwitchPopoutView.CoreWebView2;
        if (core is null)
        {
            return;
        }

        try
        {
            await core.ExecuteScriptAsync(TwitchMessagesOnlyScript);
        }
        catch (System.Runtime.InteropServices.COMException ex)
        {
            ViewModel.UpdateDiagnostic($"Não foi possível ocultar o layout do chat da Twitch: {ex.Message}");
        }
    }

    private async Task ApplyYouTubeMessagesOnlyAsync()
    {
        var core = YouTubePublicChatView.CoreWebView2;
        if (core is null)
        {
            return;
        }

        try
        {
            await core.ExecuteScriptAsync(YouTubeMessagesOnlyScript);
        }
        catch (System.Runtime.InteropServices.COMException ex)
        {
            ViewModel.UpdateDiagnostic($"Não foi possível ajustar a exibição do chat público do YouTube: {ex.Message}");
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
                TwitchPopoutView.Visibility = Visibility.Visible;
                if (!_youTubeChatClient.IsConnected && !_youTubePublicChatActive)
                {
                    viewModel.Messages.Clear();
                }

                RefreshChatPanels();
                UpdateChatOnlyControlsVisibility();
                SetConnected(true);
                viewModel.ConnectionStatus = $"Chat público · #{channel}";
                viewModel.UpdateDiagnostic($"Chat público da Twitch aberto para #{channel}; as mensagens são integradas ao feed do aplicativo.");
                _settings.Save();
                return;
            }

            ChatMessagesScrollViewer.Visibility = Visibility.Visible;
            TwitchPopoutView.CoreWebView2?.Stop();
            TwitchPopoutView.Visibility = Visibility.Collapsed;
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
                TwitchAuthorizationStatus.Text = "Clique em Conectar para autorizar sua conta Twitch.";
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
            viewModel.ConnectionStatus = $"Conectado · {channels.Count} canal(is)";
            viewModel.UpdateDiagnostic($"Conectado a: {string.Join(", ", channels.Select(channel => $"#{channel}"))}. O filtro de canais, usuários e comandos está ativo.");
            _settings.Save();
            TwitchAuthorizationStatus.Text = $"Autorizado como @{credentials.Username}.";
            TwitchDeviceCodeText.Visibility = Visibility.Collapsed;
            TwitchOpenAuthorizationButton.Visibility = Visibility.Collapsed;
        }
        catch (OperationCanceledException) when (_lifetimeCts.IsCancellationRequested || _authorizationCts?.IsCancellationRequested == true)
        {
            SetConnected(false);
            ViewModel.ConnectionStatus = "Autorização cancelada";
            TwitchAuthorizationStatus.Text = "Autorização cancelada.";
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
            TwitchCancelAuthorizationButton.Visibility = Visibility.Collapsed;
            Interlocked.Exchange(ref _connectInProgress, 0);
        }
    }

    private async Task<TwitchCredentials> AuthorizeTwitchAsync()
    {
        var authorizationCts = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCts.Token);
        _authorizationCts = authorizationCts;
        TwitchAuthorizationStatus.Text = "Aguardando autorização na Twitch...";
        TwitchDeviceCodeText.Visibility = Visibility.Collapsed;
        TwitchOpenAuthorizationButton.Visibility = Visibility.Collapsed;
        TwitchCancelAuthorizationButton.Visibility = Visibility.Visible;

        var progress = new Progress<TwitchDeviceAuthorization>(deviceAuthorization =>
        {
            if (authorizationCts.IsCancellationRequested ||
                _settings.ConnectionMode != ChatConnectionMode.AuthenticatedIrc)
            {
                return;
            }

            _twitchVerificationUri = deviceAuthorization.VerificationUri;
            TwitchAuthorizationStatus.Text = $"Acesse a página da Twitch e informe este código (expira em {Math.Max(1, deviceAuthorization.ExpiresIn / 60)} min):";
            TwitchDeviceCodeText.Text = deviceAuthorization.UserCode;
            TwitchDeviceCodeText.Visibility = Visibility.Visible;
            TwitchOpenAuthorizationButton.Visibility = Visibility.Visible;
            SettingsPopup.IsOpen = true;
            ViewModel.SetSettingsVisible(true);
            ShowPreferencesPage(ConnectionsPreferencesPage, ConnectionsPreferencesButton);
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
            if (_isConnected && !_chatClient.IsConnected)
            {
                await ConnectAsync(showError: false, allowAuthorization: false);
            }
        }
        finally
        {
            Interlocked.Exchange(ref _reconnectScheduled, 0);
        }
    }

    private void ApplyAppearance()
    {
        var chatOnly = _settings.IsTransparent;
        ResizeMode = ResizeMode.CanResize;
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
        RootBorder.Background = chatOnly
            ? Brushes.Transparent
            : new SolidColorBrush(Color.FromArgb(alpha, 10, 18, 28));
        RootBorder.BorderBrush = chatOnly
            ? Brushes.Transparent
            : new SolidColorBrush(Color.FromArgb(120, 120, 150, 180));
        RootBorder.BorderThickness = chatOnly ? new Thickness(0) : new Thickness(1);
        RootBorder.CornerRadius = chatOnly ? new CornerRadius(0) : new CornerRadius(18);
        RootBorder.Effect = chatOnly ? null : new System.Windows.Media.Effects.DropShadowEffect
        {
            Color = Color.FromRgb(141, 91, 218),
            BlurRadius = 26,
            ShadowDepth = 0,
            Opacity = 0.22
        };
        MainLayoutGrid.Margin = chatOnly ? new Thickness(0) : new Thickness(8);
        Toolbar.Visibility = chatOnly ? Visibility.Collapsed : Visibility.Visible;
        SettingsPanel.Visibility = ViewModel.SettingsVisibility;
        ChatContainer.Background = chatOnly ? Brushes.Transparent : new SolidColorBrush(Color.FromRgb(13, 20, 32));
        ChatContainer.BorderBrush = chatOnly ? Brushes.Transparent : new SolidColorBrush(Color.FromRgb(45, 55, 71));
        ChatContainer.BorderThickness = chatOnly ? new Thickness(0) : new Thickness(1);
        ChatContainer.CornerRadius = chatOnly ? new CornerRadius(0) : new CornerRadius(12);
        ChatMessagesScrollViewer.Padding = chatOnly ? new Thickness(0, 38, 0, 0) : new Thickness(0);
        TwitchPopoutView.Margin = chatOnly ? new Thickness(0, 38, 0, 0) : new Thickness(0);
        YouTubePublicChatView.Margin = chatOnly ? new Thickness(0, 38, 0, 0) : new Thickness(0);
        Toolbar.Opacity = 0.82;
        UpdateClickThroughStyle();
    }

    private void UpdateClickThroughStyle()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        var extendedStyle = NativeMethods.GetWindowLong(handle, NativeMethods.GwlExStyle);
        var enableClickThrough = _settings.ClickThroughEnabled &&
                                 _settings.IsTransparent &&
                                 !_cursorOverChatOnlyGear &&
                                 !_cursorOverResizeEdge &&
                                 !_chatOnlyGearDragActive;
        extendedStyle = enableClickThrough
            ? extendedStyle | NativeMethods.WsExTransparent
            : extendedStyle & ~NativeMethods.WsExTransparent;
        NativeMethods.SetWindowLong(handle, NativeMethods.GwlExStyle, extendedStyle);
    }

    private static bool ShouldDisplayMessage(ChatMessage message, AppSettings settings)
    {
        var origin = NormalizeChannel(message.EffectiveOrigin);
        var main = NormalizeChannel(settings.MainChannel);
        var selected = settings.SelectedChannels.Select(NormalizeChannel).Where(x => !string.IsNullOrEmpty(x)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var blocked = settings.BlockedChannels.Select(NormalizeChannel).Where(x => !string.IsNullOrEmpty(x)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var blockedUsers = settings.BlockedUsers
            .Select(user => user.Trim().TrimStart('@').ToLowerInvariant())
            .Where(user => !string.IsNullOrEmpty(user))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (blockedUsers.Contains(message.LoginName.Trim().TrimStart('@')) ||
            blockedUsers.Contains(message.DisplayName.Trim().TrimStart('@')))
        {
            return false;
        }

        if (settings.HideCommands && message.Message.TrimStart().StartsWith('!'))
        {
            return false;
        }

        if (message.Platform == ChatPlatform.YouTube)
        {
            return true;
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

        if (blocked.Contains(origin))
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

    private static ChatRowView CreateChatRow(ChatMessage message, AppSettings settings)
    {
        var color = string.IsNullOrWhiteSpace(message.RawMetadata) ? Colors.White : TryParseColor(message.RawMetadata);
        var (platformImage, platformName) = message.Platform switch
        {
            ChatPlatform.YouTube => (ChatRowView.YouTubeImage, "YouTube"),
            _ => (ChatRowView.TwitchImage, "Twitch")
        };
        var (badgeImage, badgeName) = !settings.ShowBadges
            ? (null, string.Empty)
            : message.IsModerator && settings.ShowModerators
                ? (ChatRowView.ModeratorImage, "Moderador")
                : message.IsVip && settings.ShowVips
                    ? (ChatRowView.VipImage, "VIP")
                : message.IsSubscriber
                    ? (ChatRowView.SubscriberImage, "Inscrito")
                    : (null, string.Empty);
        var showSubscriberBadge = settings.ShowBadges &&
                                  message.IsSubscriber &&
                                  badgeImage != ChatRowView.SubscriberImage;

        var row = new ChatRowView
        {
            PlatformImage = platformImage,
            PlatformName = platformName,
            BadgeImage = badgeImage,
            BadgeName = badgeName,
            SubscriberBadgeImage = showSubscriberBadge ? ChatRowView.SubscriberImage : null,
            SubscriberBadgeName = showSubscriberBadge ? "Inscrito" : string.Empty,
            FullTimestampText = settings.ShowTimestamps ? $"{DateTime.Now:HH:mm} " : string.Empty,
            DisplayName = settings.ShowUserNames ? message.DisplayName : string.Empty,
            BadgeText = string.Empty,
            NameSeparator = settings.ShowUserNames ? ": " : string.Empty,
            BadgeVisibility = badgeImage is null ? Visibility.Collapsed : Visibility.Visible,
            SubscriberBadgeVisibility = showSubscriberBadge ? Visibility.Visible : Visibility.Collapsed,
            DefaultNameBrush = new SolidColorBrush(color),
            Text = message.Message
        };
        row.SetTransparentAppearance(settings.IsTransparent);
        return row;
    }

    private static Color TryParseColor(string rawMetadata)
    {
        var key = "color=";
        var index = rawMetadata.IndexOf(key, StringComparison.OrdinalIgnoreCase);
        if (index >= 0)
        {
            var value = rawMetadata[(index + key.Length)..];
            if (value.Contains(';', StringComparison.Ordinal))
            {
                value = value[..value.IndexOf(';')];
            }

            if (!string.IsNullOrWhiteSpace(value) && value.StartsWith("#") && value.Length == 7)
            {
                return (Color)ColorConverter.ConvertFromString(value)!;
            }
        }

        return Colors.White;
    }

}

internal static class NativeMethods
{
    [StructLayout(LayoutKind.Sequential)]
    public struct Point
    {
        public int X;
        public int Y;
    }

    public const int ModAlt = 0x0001;
    public const int ModControl = 0x0002;
    public const int ModShift = 0x0004;
    public const int GwlExStyle = -20;
    public const int WsExTransparent = 0x00000020;
    public const int WmNcHitTest = 0x0084;
    public const int HtLeft = 10;
    public const int HtRight = 11;
    public const int HtTop = 12;
    public const int HtTopLeft = 13;
    public const int HtTopRight = 14;
    public const int HtBottom = 15;
    public const int HtBottomLeft = 16;
    public const int HtBottomRight = 17;

    [DllImport("user32.dll")]
    public static extern bool RegisterHotKey(IntPtr hWnd, int id, int fsModifiers, int vk);

    [DllImport("user32.dll")]
    public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetCursorPos(out Point point);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    public static extern int GetWindowLong(IntPtr hWnd, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")]
    public static extern int SetWindowLong(IntPtr hWnd, int index, int newStyle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetForegroundWindow(IntPtr hWnd);

}
