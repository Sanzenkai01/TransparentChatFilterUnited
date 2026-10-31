using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Windows;
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

namespace TransparentChatFilter;

public sealed class OverlayViewModel : INotifyPropertyChanged
{
    private readonly AppSettings _settings;

    public OverlayViewModel(AppSettings settings)
    {
        _settings = settings;
        Messages = new ObservableCollection<ChatRowView>();
        RecentChannels = new ObservableCollection<string>(
            settings.RecentChannels ?? Array.Empty<string>());
        MainChannel = settings.MainChannel;
        ConnectionStatus = "Desconectado";
        SelectedChannelsText = string.Join(", ", settings.SelectedChannels);
        BlockedChannelsText = string.Join(", ", settings.BlockedChannels);
        BlockedUsersText = string.Join(", ", settings.BlockedUsers);
        BlockedTermsText = settings.BlockedTerms;
        HighlightTermsText = settings.HighlightTerms;
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
    public ObservableCollection<string> RecentChannels { get; }

    public AppSettings Settings => _settings;

    public string MainChannel
    {
        get => _mainChannelText;
        set
        {
            if (string.Equals(_mainChannelText, value, StringComparison.Ordinal))
            {
                return;
            }

            _mainChannelText = value;
            _settings.MainChannel = NormalizedChannel(value);
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
            OnPropertyChanged(nameof(IsAuthenticatedIrc));
        }
    }

    public bool IsAuthenticatedIrc => _settings.ConnectionMode == ChatConnectionMode.AuthenticatedIrc;

    public string SelectedChannelsText
    {
        get => _selectedChannelsText;
        set
        {
            _selectedChannelsText = value;
            _settings.SelectedChannels = ParseList(value);
        }
    }

    public string BlockedChannelsText
    {
        get => _blockedChannelsText;
        set
        {
            _blockedChannelsText = value;
            _settings.BlockedChannels = ParseList(value);
        }
    }

    public string BlockedUsersText
    {
        get => _blockedUsersText;
        set
        {
            _blockedUsersText = value;
            _settings.BlockedUsers = ParseUserList(value);
        }
    }

    public string BlockedTermsText
    {
        get => _settings.BlockedTerms;
        set
        {
            _settings.BlockedTerms = value;
            OnPropertyChanged();
        }
    }

    public string HighlightTermsText
    {
        get => _settings.HighlightTerms;
        set
        {
            _settings.HighlightTerms = value;
            OnPropertyChanged();
        }
    }

    public bool EnableSound
    {
        get => _settings.EnableSound;
        set
        {
            _settings.EnableSound = value;
            OnPropertyChanged();
        }
    }

    public bool NotifyNewMessages
    {
        get => _settings.NotifyNewMessages;
        set
        {
            if (_settings.NotifyNewMessages == value)
            {
                return;
            }

            _settings.NotifyNewMessages = value;
            OnPropertyChanged();
        }
    }

    public int NotificationVolumePercent
    {
        get => Math.Clamp(_settings.NotificationVolumePercent, 0, 100);
        set
        {
            var volume = Math.Clamp(value, 0, 100);
            if (_settings.NotificationVolumePercent == volume)
            {
                return;
            }

            _settings.NotificationVolumePercent = volume;
            OnPropertyChanged();
        }
    }

    public bool DimOtherChannelMessages
    {
        get => _settings.DimOtherChannelMessages;
        set
        {
            if (_settings.DimOtherChannelMessages == value)
            {
                return;
            }

            _settings.DimOtherChannelMessages = value;
            OnPropertyChanged();
        }
    }

    public double TextOpacity
    {
        get => Math.Clamp(_settings.TextOpacity, 0.2, 1.0);
        set
        {
            var opacity = Math.Clamp(value, 0.2, 1.0);
            if (Math.Abs(_settings.TextOpacity - opacity) < 0.001)
            {
                return;
            }

            _settings.TextOpacity = opacity;
            OnPropertyChanged();
        }
    }

    public bool UseMessageBackgroundOpacity
    {
        get => _settings.UseMessageBackgroundOpacity;
        set
        {
            if (_settings.UseMessageBackgroundOpacity == value)
            {
                return;
            }

            _settings.UseMessageBackgroundOpacity = value;
            OnPropertyChanged();
        }
    }

    public double MessageBackgroundOpacity
    {
        get => Math.Clamp(_settings.MessageBackgroundOpacity, 0.1, 1.0);
        set
        {
            var opacity = Math.Clamp(value, 0.1, 1.0);
            if (Math.Abs(_settings.MessageBackgroundOpacity - opacity) < 0.001)
            {
                return;
            }

            _settings.MessageBackgroundOpacity = opacity;
            OnPropertyChanged();
        }
    }

    public int MessageSpacing
    {
        get => Math.Clamp(_settings.MessageSpacing, 0, 20);
        set
        {
            var spacing = Math.Clamp(value, 0, 20);
            if (_settings.MessageSpacing == spacing)
            {
                return;
            }

            _settings.MessageSpacing = spacing;
            OnPropertyChanged();
        }
    }

    public string AccentColor
    {
        get => _settings.AccentColor;
        set
        {
            if (!IsValidColor(value))
            {
                return;
            }

            _settings.AccentColor = value;
            OnPropertyChanged();
        }
    }

    public string MessageBackgroundColor
    {
        get => _settings.MessageBackgroundColor;
        set
        {
            if (!IsValidColor(value))
            {
                return;
            }

            _settings.MessageBackgroundColor = value;
            OnPropertyChanged();
        }
    }

    private string _connectionStatus = string.Empty;
    private string _mainChannelText = string.Empty;
    private string _selectedChannelsText = string.Empty;
    private string _blockedChannelsText = string.Empty;
    private string _blockedUsersText = string.Empty;
    private bool _isConnected;
    private bool _isTwitchConnected;
    private bool _isConnecting;

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

    public bool IsConnecting
    {
        get => _isConnecting;
        set
        {
            if (_isConnecting == value)
            {
                return;
            }

            _isConnecting = value;
            OnPropertyChanged();
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

    public bool UseTwitchNameColors
    {
        get => _settings.UseTwitchNameColors;
        set
        {
            _settings.UseTwitchNameColors = value;
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
        get => Math.Clamp(_settings.BackgroundOpacity, 0.15, 1.0);
        set
        {
            _settings.BackgroundOpacity = Math.Clamp(value, 0.15, 1.0);
            OnPropertyChanged();
        }
    }

    private static bool IsValidColor(string value)
    {
        try
        {
            return value.StartsWith('#') && value.Length == 7 &&
                   ColorConverter.ConvertFromString(value) is Color;
        }
        catch (FormatException)
        {
            return false;
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
    private Brush _nameBrush = Brushes.White;
    private string _timestampText = string.Empty;

    public static ImageSource TwitchImage { get; } = new BitmapImage(new Uri("pack://application:,,,/twitch.png"));
    public ImageSource PlatformImage { get; set; } = TwitchImage;
    public string ChannelImageUrl { get; set; } = string.Empty;
    public Brush MessageBackgroundBrush { get; set; } = Brushes.Transparent;
    public IReadOnlyList<TwitchChatBadge> Badges { get; set; } = Array.Empty<TwitchChatBadge>();
    public string PlatformName { get; set; } = "Twitch";
    public ChatMessage SourceMessage { get; set; } = new();
    public IReadOnlyList<TwitchEmoteRange> Emotes { get; set; } = Array.Empty<TwitchEmoteRange>();
    public bool IsSystemEvent { get; set; }
    public string SystemText { get; set; } = string.Empty;
    public Thickness RowMargin { get; set; }
    public double RowOpacity { get; set; } = 1;
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
    public event PropertyChangedEventHandler? PropertyChanged;

    public void SetTransparentAppearance(bool enabled)
    {
        TimestampText = enabled ? string.Empty : FullTimestampText;
        NameBrush = DefaultNameBrush;
    }
}

public partial class MainWindow : Window
{
    private const int WmHotkey = 0x0312;
    private const long MessageNotificationSoundCooldownMilliseconds = 500;
    private static readonly HttpClient TwitchBadgeImageHttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(10)
    };
    private readonly TwitchChatClient _chatClient = new();
    private readonly TwitchBadgeClient _twitchBadgeClient = new();
    private readonly TwitchChannelClient _twitchChannelClient = new();
    private readonly TwitchOAuthClient _twitchOAuthClient = new();
    private readonly AppSettings _settings;
    private readonly CancellationTokenSource _lifetimeCts = new();
    private readonly List<ChatMessage> _chatHistory = new();
    private readonly Dictionary<string, ImageSource> _twitchEmoteImageCache = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Task<ImageSource>> _twitchBadgeImageCache = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Task<ImageSource>> _twitchChannelImageCache = new(StringComparer.Ordinal);
    private TwitchCredentials? _twitchCredentials;
    private CancellationTokenSource? _authorizationCts;
    private Uri? _twitchVerificationUri;
    private int _connectInProgress;
    private int _hotKeyId = 9000;
    private bool _isConnected;
    private bool _twitchConnectionActive;
    private bool _sharedChatFilterWarningShown;
    private int _reconnectScheduled;
    private bool _windowPositionReady;
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
    private bool _settingsPopupDragging;
    private Point _settingsPopupDragStart;
    private Point _settingsPopupDragOrigin;
    private bool _chatOnlyGearPopupOffsetNudge;
    private NativeMethods.Point _chatOnlyGearDragStart;
    private double _chatOnlyGearWindowStartLeft;
    private double _chatOnlyGearWindowStartTop;
    private bool _popoutScriptRegistered;
    private bool _popoutNavigationHandlerRegistered;
    private bool _twitchCaptureScriptRegistered;
    private bool _twitchPublicChatActive;
    private bool _channelUpdatePending;
    private int _connectionModeChangeVersion;
    private long _lastMessageNotificationSoundTimestamp;
    private const string TwitchCaptureScript = """
        (() => {
            if (window.__transparentChatFilterTwitchCapture) return;
            const capturedLines = new WeakMap();
            const messageSelector = [
                '[data-a-target="chat-line-message"]',
                '[data-test-selector="chat-line-message"]',
                '.chat-line__message'
            ].join(',');
            const start = () => {
                if (!document.documentElement) return false;
                const send = () => {
                    document.querySelectorAll(messageSelector).forEach(line => {
                        const user = line.querySelector([
                            '[data-a-target="chat-message-username"]',
                            '[data-test-selector="message-username"]',
                            '.chat-author__display-name'
                        ].join(','));
                        const body = line.querySelector([
                            '[data-a-target="chat-line-message-body"]',
                            '[data-test-selector="message-body"]',
                            '.text-fragment'
                        ].join(','));
                        if (!user || !body) return;
                        const displayName = (user.textContent || '').trim();
                        const message = (body.textContent || '').trim();
                        if (!displayName || !message) return;
                        const precedingImages = Array.from(line.querySelectorAll('img'))
                            .filter(image =>
                                (user.compareDocumentPosition(image) & Node.DOCUMENT_POSITION_PRECEDING) !== 0);
                        const sharedChatImage = line.querySelector([
                            '[data-a-target*="shared-chat"] img',
                            '[data-test-selector*="shared-chat"] img',
                            '[class*="shared-chat"] img',
                            '[data-a-target*="sharedChat"] img',
                            '[data-test-selector*="sharedChat"] img',
                            '[data-a-target*="channel-badge"] img',
                            '[data-test-selector*="channel-badge"] img',
                            'img[data-a-target*="shared-chat"]',
                            'img[data-test-selector*="shared-chat"]',
                            'img[data-a-target*="sharedChat"]',
                            'img[data-test-selector*="sharedChat"]'
                        ].join(','));
                        const getImageUrl = image =>
                            image.currentSrc || image.src || image.getAttribute('data-src') ||
                            image.getAttribute('src') || '';
                        const isChannelProfileImage = image => {
                            const imageUrl = getImageUrl(image);
                            const imagePath = imageUrl.split(/[?#]/, 1)[0];
                            const accessibleName = [
                                image.alt,
                                image.getAttribute('aria-label'),
                                image.title,
                                image.parentElement?.getAttribute('aria-label'),
                                image.parentElement?.title
                            ].filter(Boolean).join(' ').toLowerCase();
                            return /\/(?:jtv_user_pictures|user-pictures|channel_images|profiles)\//i.test(imagePath) ||
                                /shared chat|channel (?:profile|picture|avatar)|channel icon/i.test(accessibleName);
                        };
                        const channelImage = sharedChatImage || precedingImages.find(isChannelProfileImage);
                        const channelImageUrl = channelImage
                            ? getImageUrl(channelImage)
                            : '';
                        const signature = `${displayName}\n${message}\n${channelImageUrl}`;
                        if (capturedLines.get(line) === signature) return;
                        const badges = precedingImages
                            .map(image => {
                                const imageUrl = getImageUrl(image);
                                if (!/^https:\/\/(?:[^/]+\.)?(?:jtvnw\.net|twitch\.tv)\//i.test(imageUrl)) return null;
                                const title = (image.alt || image.getAttribute('aria-label') ||
                                    image.title || image.parentElement?.getAttribute('aria-label') ||
                                    image.parentElement?.title || 'Twitch badge').trim();
                                const label = title.toLowerCase();
                                const setId = label.includes('moderator') ? 'moderator' :
                                    label.includes('vip') ? 'vip' :
                                    label.includes('subscriber') ? 'subscriber' : label;
                                return { setId, title, imageUrl };
                            })
                            .filter(badge => badge && badge.imageUrl && badge.imageUrl !== channelImageUrl);
                        const pathSegments = location.pathname.split('/').filter(Boolean);
                        const channel = pathSegments[0] === 'popout' ? pathSegments[1] || '' : pathSegments[0] || '';
                        window.chrome.webview.postMessage({
                            kind:'message',
                            displayName, message, channel, channelImageUrl,
                            isModerator:badges.some(badge => badge.setId === 'moderator'),
                            isVip:badges.some(badge => badge.setId === 'vip'),
                            isSubscriber:badges.some(badge => badge.setId === 'subscriber'),
                            badges
                        });
                        capturedLines.set(line, signature);
                    });
                };
                new MutationObserver(send).observe(document.documentElement, {
                    childList:true, subtree:true, characterData:true
                });
                window.__transparentChatFilterTwitchCapture = true;
                window.chrome.webview.postMessage({ kind:'capture-ready' });
                setInterval(send, 1000);
                send();
                return true;
            };
            if (!start()) {
                document.addEventListener('DOMContentLoaded', start, { once:true });
            }
        })();
        """;
    private const string TwitchMessagesOnlyScript = """
        (() => {
            const existingApply = window.__transparentChatFilterTwitchScrollFix;
            if (typeof existingApply === 'function') {
                existingApply();
                return;
            }
            const styleId = 'transparent-chat-filter-messages-only';
            const messageSelector = [
                '[data-test-selector="chat-scrollable-area__message-container"]',
                '[data-a-target="chat-scrollable-area__message-container"]',
                '[role="log"].chat-scrollable-area__message-container'
            ].join(',');
            const css = `
                html, body { width:100%!important; height:100%!important; margin:0!important; overflow:hidden!important; background:transparent!important; }
                *, *::before, *::after { scrollbar-width:none!important; -ms-overflow-style:none!important; }
                *::-webkit-scrollbar { display:none!important; width:0!important; height:0!important; }
                body * { visibility:hidden!important; }
                ${messageSelector} {
                    position:fixed!important; inset:0!important; z-index:2147483647!important;
                    display:block!important; width:100vw!important; height:100vh!important;
                    max-height:100vh!important; overflow-x:hidden!important; overflow-y:auto!important;
                    scrollbar-width:none!important; -ms-overflow-style:none!important;
                    margin:0!important; padding:0!important; background:transparent!important;
                    visibility:hidden!important;
                }
                ${messageSelector} [data-a-target="chat-line-message"] {
                    display:block!important;
                    width:max-content!important;
                    max-width:100%!important;
                    box-sizing:border-box!important;
                    margin:2px 0!important;
                    padding:3px 7px!important;
                    border-radius:4px!important;
                    background:rgba(0,0,0,0.72)!important;
                }
                ${messageSelector} [data-a-target="chat-line-message"] * {
                    background-color:transparent!important;
                    -webkit-text-stroke:0!important;
                    text-shadow:0 1px 1px rgba(0,0,0,0.95), 0 0 3px rgba(0,0,0,0.8)!important;
                    visibility:hidden!important;
                }
            `;
            const observedContainers = new WeakSet();
            const scrollToLatest = container => {
                const scroll = () => { container.scrollTop = container.scrollHeight; };
                requestAnimationFrame(scroll);
                setTimeout(scroll, 80);
            };
            const apply = () => {
                const messages = document.querySelector(messageSelector);
                const root = document.head || document.documentElement;
                if (root) {
                    let style = document.getElementById(styleId);
                    if (!style) {
                        style = document.createElement('style');
                        style.id = styleId;
                        root.appendChild(style);
                    }
                    if (style.textContent !== css) {
                        style.textContent = css;
                    }
                }
                if (!messages) return;
                if (!observedContainers.has(messages)) {
                    observedContainers.add(messages);
                    new MutationObserver(() => scrollToLatest(messages)).observe(messages, {
                        childList:true, subtree:true, characterData:true
                    });
                }
                scrollToLatest(messages);
            };
            const observer = new MutationObserver(apply);
            observer.observe(document, { childList:true, subtree:true });
            window.__transparentChatFilterTwitchScrollFix = apply;
            apply();
        })();
        """;

    public MainWindow()
    {
        _settings = AppSettings.Load();
        _settings.IsTransparent = false;
        DataContext = new OverlayViewModel(_settings);
        InitializeComponent();
        MainChannelComboBox.ItemsSource = ViewModel.RecentChannels;
        AppVersionText.Text = $"Versão {GetType().Assembly.GetName().Version?.ToString(3) ?? "desconhecida"}";
        UpdateColorPreview(AccentColorInput.Text, AccentColorPreviewDot, AccentColorPreviewText);
        UpdateColorPreview(MessageBackgroundColorInput.Text, MessageBackgroundColorPreviewDot, MessageBackgroundColorPreviewText);
        RestoreWindowPosition();
        _windowPositionSaveTimer.Tick += WindowPositionSaveTimer_Tick;
        _channelUpdateTimer.Tick += ChannelUpdateTimer_Tick;
        _settingsSaveTimer.Tick += (_, _) =>
        {
            _settingsSaveTimer.Stop();
            ViewModel.SaveSettings();
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
        FilterModeComboBox.SelectedIndex = (int)_settings.FilterMode;
        ConnectionModeComboBox.SelectedIndex = (int)_settings.ConnectionMode;
        NotificationSoundComboBox.SelectedIndex = (int)_settings.NotificationSound;
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
        _settingsSaveTimer.Stop();
        ViewModel.SaveSettings();
        SaveWindowPosition();
        _lifetimeCts.Cancel();
        _authorizationCts?.Cancel();
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

    private void ToggleSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        ToggleSettingsPopup(!SettingsPopup.IsOpen);
    }

    private void CloseSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        ToggleSettingsPopup(false);
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

    private void SettingsResizeThumb_DragDelta(object sender, DragDeltaEventArgs e)
    {
        var maximumWidth = Math.Max(SettingsPanel.MinWidth, SystemParameters.WorkArea.Width - 32);
        var maximumHeight = Math.Max(SettingsPanel.MinHeight, SystemParameters.WorkArea.Height - 32);
        SettingsPanel.Width = Math.Clamp(
            SettingsPanel.Width + e.HorizontalChange,
            SettingsPanel.MinWidth,
            maximumWidth);
        SettingsPanel.Height = Math.Clamp(
            SettingsPanel.Height + e.VerticalChange,
            SettingsPanel.MinHeight,
            maximumHeight);
    }

    private void SettingsPopupDrag_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement dragSurface
            || e.ChangedButton != MouseButton.Left
            || IsWindowControl(e.OriginalSource as DependencyObject))
        {
            return;
        }

        _settingsPopupDragStart = dragSurface.PointToScreen(e.GetPosition(dragSurface));
        _settingsPopupDragOrigin = new Point(SettingsPopup.HorizontalOffset, SettingsPopup.VerticalOffset);
        _settingsPopupDragging = dragSurface.CaptureMouse();
        e.Handled = true;
    }

    private void SettingsPopupDrag_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_settingsPopupDragging || sender is not FrameworkElement dragSurface)
        {
            return;
        }

        var currentPosition = dragSurface.PointToScreen(e.GetPosition(dragSurface));
        var delta = currentPosition - _settingsPopupDragStart;
        if (PresentationSource.FromVisual(dragSurface)?.CompositionTarget is { } target)
        {
            delta = target.TransformFromDevice.Transform(delta);
        }

        SettingsPopup.HorizontalOffset = _settingsPopupDragOrigin.X + delta.X;
        SettingsPopup.VerticalOffset = _settingsPopupDragOrigin.Y + delta.Y;
        e.Handled = true;
    }

    private void SettingsPopupDrag_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_settingsPopupDragging || sender is not FrameworkElement dragSurface)
        {
            return;
        }

        _settingsPopupDragging = false;
        dragSurface.ReleaseMouseCapture();
        e.Handled = true;
    }

    private void ChatPreferencesButton_Click(object sender, RoutedEventArgs e) => ShowPreferencesPage(ChatPreferencesPage, ChatPreferencesButton);

    private void ConnectionsPreferencesButton_Click(object sender, RoutedEventArgs e) => ShowPreferencesPage(ConnectionsPreferencesPage, ConnectionsPreferencesButton);

    private void WidgetsPreferencesButton_Click(object sender, RoutedEventArgs e) => ShowPreferencesPage(WidgetsPreferencesPage, WidgetsPreferencesButton);

    private void AboutPreferencesButton_Click(object sender, RoutedEventArgs e) => ShowPreferencesPage(AboutPreferencesPage, AboutPreferencesButton);

    private void ShowPreferencesPage(FrameworkElement page, Button activeButton)
    {
        ChatPreferencesPage.Visibility = Visibility.Collapsed;
        ConnectionsPreferencesPage.Visibility = Visibility.Collapsed;
        WidgetsPreferencesPage.Visibility = Visibility.Collapsed;
        AboutPreferencesPage.Visibility = Visibility.Collapsed;
        page.Visibility = Visibility.Visible;

        foreach (var button in new[]
                 {
                     ChatPreferencesButton,
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
        var transition = new TranslateTransform(0, 7);
        page.RenderTransform = transition;
        page.Opacity = 0;
        page.UpdateLayout();
        page.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        });
        transition.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(7, 0, TimeSpan.FromMilliseconds(180))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        });
        if (page is ScrollViewer scrollViewer)
        {
            scrollViewer.ScrollToTop();
        }
    }

    private void SavePreferences_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsInitialized)
        {
            return;
        }

        if (sender is CheckBox checkBox)
        {
            checkBox.GetBindingExpression(ToggleButton.IsCheckedProperty)?.UpdateSource();
        }

        ApplyAppearance();
        RefreshMessageFeed();
        ViewModel.SaveSettings();
    }

    private void NotificationSoundComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsInitialized ||
            NotificationSoundComboBox.SelectedIndex < 0 ||
            !Enum.IsDefined((MessageNotificationSound)NotificationSoundComboBox.SelectedIndex))
        {
            return;
        }

        var selectedSound = (MessageNotificationSound)NotificationSoundComboBox.SelectedIndex;
        if (_settings.NotificationSound == selectedSound)
        {
            return;
        }

        _settings.NotificationSound = selectedSound;
        ViewModel.SaveSettings();
        _ = PlayNotificationSoundAsync(selectedSound, _settings.NotificationVolumePercent);
    }

    private void ComboBoxSurface_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Grid grid ||
            grid.TemplatedParent is not ComboBox comboBox ||
            e.GetPosition(grid).X >= grid.ActualWidth - 28)
        {
            return;
        }

        comboBox.Focus();
        comboBox.IsDropDownOpen = true;
        e.Handled = true;
    }

    private void NotificationSoundPreviewButton_Click(object sender, RoutedEventArgs e)
    {
        if (NotificationSoundComboBox.SelectedIndex >= 0 &&
            Enum.IsDefined((MessageNotificationSound)NotificationSoundComboBox.SelectedIndex))
        {
            _ = PlayNotificationSoundAsync(
                (MessageNotificationSound)NotificationSoundComboBox.SelectedIndex,
                _settings.NotificationVolumePercent);
        }
    }

    private void NotificationVolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (IsInitialized)
        {
            ViewModel.SaveSettings();
        }
    }

    private void FontSizeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (IsInitialized)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                RefreshMessageFeed();
                ViewModel.SaveSettings();
            }), DispatcherPriority.DataBind);
        }
    }

    private void SaveTextPreference_Changed(object sender, TextChangedEventArgs e)
    {
        if (IsInitialized)
        {
            RefreshMessageFeed();
            QueueSettingsSave();
            if (ReferenceEquals(sender, SelectedChannelsTextBox))
            {
                ScheduleChannelUpdate();
            }
        }
    }

    private void RecentChannelsComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (MainChannelComboBox.SelectedItem is string channel)
        {
            ViewModel.MainChannel = channel;
            ScheduleChannelUpdate();
            QueueSettingsSave();
        }
    }

    private void MainChannelTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!IsInitialized)
        {
            return;
        }

        RefreshMessageFeed();
        QueueSettingsSave();
        ScheduleChannelUpdate();
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

            ViewModel.SaveSettings();
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
        _settingsSaveTimer.Stop();
        _settingsSaveTimer.Start();
    }

    private void AppearanceSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!IsInitialized)
        {
            return;
        }

        RefreshMessageFeed();
        ViewModel.SaveSettings();
    }

    private void AccentColor_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!IsInitialized)
        {
            return;
        }

        var isValid = AccentColorInput.Text.Trim() is { Length: 7 } colorText &&
                      colorText.StartsWith('#') &&
                      ParseHexColor(colorText) is not null;
        AccentColorStatus.Text = isValid ? string.Empty : "Use uma cor hexadecimal como #5EA2FF.";
        AccentColorStatus.Visibility = isValid ? Visibility.Collapsed : Visibility.Visible;
        if (isValid)
        {
            var selectedColor = AccentColorInput.Text.Trim();
            ViewModel.AccentColor = selectedColor;
            UpdateColorPreview(selectedColor, AccentColorPreviewDot, AccentColorPreviewText);
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

        var colorText = MessageBackgroundColorInput.Text.Trim();
        var isValid = colorText.Length == 7 &&
                      colorText.StartsWith('#') &&
                      ParseHexColor(colorText) is not null;
        MessageBackgroundColorStatus.Text = isValid ? string.Empty : "Use uma cor hexadecimal como #161D2C.";
        MessageBackgroundColorStatus.Visibility = isValid ? Visibility.Collapsed : Visibility.Visible;
        if (isValid)
        {
            ViewModel.MessageBackgroundColor = colorText;
            UpdateColorPreview(colorText, MessageBackgroundColorPreviewDot, MessageBackgroundColorPreviewText);
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

    private void ChooseAccentColorButton_Click(object sender, RoutedEventArgs e)
    {
        OpenColorPalette(AccentColorInput, (UIElement)sender);
    }

    private void ChooseMessageBackgroundColorButton_Click(object sender, RoutedEventArgs e)
    {
        OpenColorPalette(MessageBackgroundColorInput, (UIElement)sender);
    }

    private void OpenColorPalette(TextBox colorInput, UIElement placementTarget)
    {
        _activeColorInput = colorInput;
        ColorPalettePopup.PlacementTarget = placementTarget;
        ColorPalettePopup.IsOpen = true;
    }

    private void PaletteColorButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || _activeColorInput is null || button.Tag is not string color)
        {
            return;
        }

        _activeColorInput.Text = color;
        ColorPalettePopup.IsOpen = false;
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
        RefreshMessageFeed();
        _settings.Save();
    }

    private void SetConnected(bool connected)
    {
        _twitchConnectionActive = connected;
        ViewModel.IsTwitchConnected = connected;
        UpdateOverallConnectionState();
        UpdateConnectionModeUi();
    }

    private void UpdateOverallConnectionState()
    {
        _isConnected = _twitchConnectionActive;
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

    private void FilterModeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var comboBox = (ComboBox)sender;
        if (comboBox.SelectedIndex < 0)
        {
            return;
        }

        ViewModel.FilterMode = (ChatFilterMode)comboBox.SelectedIndex;
        _sharedChatFilterWarningShown = false;
        RefreshMessageFeed();
        ViewModel.SaveSettings();
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
        ApplyAppearance();
        RefreshMessageFeed();
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
        var hasTwitchAuthorization = ircMode && _twitchCredentials is not null;
        DimOtherChannelsCheckBox.Visibility = hasTwitchAuthorization
            ? Visibility.Visible
            : Visibility.Collapsed;
        ConnectionModeDescription.Text = ircMode
            ? "IRC autenticado: requer autorização da Twitch. Só neste modo estão disponíveis Chat Unido e canais adicionais, ignorar mensagens por canal ou usuário, ocultar comandos e diminuir a opacidade de outros canais. Filtros de termos e personalização visual funcionam nos dois modos."
            : "Chat público: não requer autorização e mostra apenas o canal selecionado. Filtros de termos e personalização visual continuam disponíveis; Chat Unido, canais adicionais, ignorar mensagens por canal ou usuário, ocultar comandos e opacidade por canal são exclusivos do IRC.";
        ChatFilterModeInfo.Visibility = Visibility.Collapsed;
        AuthenticatedFilterControls.Visibility = hasTwitchAuthorization
            ? Visibility.Visible
            : Visibility.Collapsed;
        FilterAuthenticationPrompt.Visibility = ircMode && !hasTwitchAuthorization
            ? Visibility.Visible
            : Visibility.Collapsed;
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
        var publicChatVisible = TwitchPopoutView.Visibility == Visibility.Visible;
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
    }

    private void PublicChat_WebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            using var document = JsonDocument.Parse(e.WebMessageAsJson);
            var root = document.RootElement;
            if (!_twitchPublicChatActive)
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

            var message = new ChatMessage
            {
                DisplayName = displayName,
                LoginName = displayName,
                Message = text,
                Channel = channel,
                OriginChannel = channel,
                ChannelImageUrl = channelImageUrl,
                Badges = badges,
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
            ex is HttpRequestException or JsonException or OperationCanceledException)
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
            DispatchToUi(() => ViewModel.UpdateDiagnostic($"Não foi possível carregar os badges da Twitch: {ex.Message}"));
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

        ViewModel.Messages.Add(CreateChatRow(message, ViewModel.Settings));
        if (ViewModel.Settings.NotifyNewMessages)
        {
            PlayNewMessageNotificationSound();
        }
        else if (ViewModel.Settings.EnableSound &&
                 ContainsAnyTerm(message.Message, ViewModel.Settings.HighlightTerms) &&
                 TryAcquireMessageNotificationSoundCooldown())
        {
            System.Media.SystemSounds.Asterisk.Play();
        }

        ScrollChatToLatestMessage();
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

        _ = PlayNotificationSoundAsync(
            ViewModel.Settings.NotificationSound,
            ViewModel.Settings.NotificationVolumePercent);
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

        ScrollChatToLatestMessage();
    }

    private void ScrollChatToLatestMessage()
    {
        Dispatcher.BeginInvoke(
            DispatcherPriority.ContextIdle,
            new Action(() =>
            {
                ChatMessagesScrollViewer.UpdateLayout();
                ChatMessagesScrollViewer.ScrollToEnd();
            }));
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

    private void RegisterPopoutNavigationHandler()
    {
        if (_popoutNavigationHandlerRegistered)
        {
            return;
        }

        TwitchPopoutView.NavigationCompleted += TwitchPopoutView_NavigationCompleted;
        _popoutNavigationHandlerRegistered = true;
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
                _ = ApplyTwitchPublicChatScriptsAsync();
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

    private async Task ApplyTwitchPublicChatScriptsAsync()
    {
        var core = TwitchPopoutView.CoreWebView2;
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
                TwitchPopoutView.Visibility = Visibility.Visible;
                viewModel.Messages.Clear();

                RefreshChatPanels();
                UpdateChatOnlyControlsVisibility();
                SetConnected(true);
                viewModel.ConnectionStatus = "Conectado";
                viewModel.UpdateDiagnostic($"Chat público da Twitch aberto para #{channel}; as mensagens são integradas ao feed do aplicativo.");
                RememberRecentChannel(channel);
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
            viewModel.ConnectionStatus = "Conectado";
            viewModel.UpdateDiagnostic($"Conectado a: {string.Join(", ", channels.Select(channel => $"#{channel}"))}. O filtro de canais, usuários e comandos está ativo.");
            RememberRecentChannel(viewModel.MainChannel);
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
        RootBorder.Background = Brushes.Transparent;
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
        TwitchPopoutView.Margin = chatOnly ? new Thickness(0, 38, 0, 0) : new Thickness(0);
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
        var ircFiltersEnabled = settings.ConnectionMode == ChatConnectionMode.AuthenticatedIrc;
        var blockedUsers = settings.BlockedUsers
            .Select(user => user.Trim().TrimStart('@').ToLowerInvariant())
            .Where(user => !string.IsNullOrEmpty(user))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (ircFiltersEnabled &&
            (blockedUsers.Contains(message.LoginName.Trim().TrimStart('@')) ||
             blockedUsers.Contains(message.DisplayName.Trim().TrimStart('@'))))
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

    private static ChatRowView CreateChatRow(ChatMessage message, AppSettings settings)
    {
        var color = settings.UseTwitchNameColors
            ? TryParseColor(message.RawMetadata) ??
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
            NameSeparator = settings.ShowUserNames && message.Kind == ChatMessageKind.Chat ? ": " : string.Empty,
            DefaultNameBrush = new SolidColorBrush(color),
            Text = message.Message
        };
        row.SetTransparentAppearance(settings.IsTransparent);
        return row;
    }

    private void ChatMessageTextBlock_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            RenderChatMessageTextBlock(sender);
        }
        catch (Exception ex) when (
            ex is InvalidOperationException or ArgumentException or FormatException or NotSupportedException)
        {
            ViewModel.UpdateDiagnostic($"Não foi possível renderizar uma mensagem do chat: {ex.Message}");
        }
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
                Foreground = new SolidColorBrush(ParseHexColor(_settings.AccentColor) ?? Color.FromRgb(169, 112, 255))
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
                FontSize = 10
            });
        }

        if (!string.IsNullOrWhiteSpace(row.ChannelImageUrl))
        {
            textBlock.Inlines.Add(CreateTwitchChannelImageInline(row));
        }

        foreach (var badge in row.Badges)
        {
            textBlock.Inlines.Add(CreateTwitchBadgeInline(badge));
        }

        if (!string.IsNullOrEmpty(row.DisplayName))
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

    private InlineUIContainer CreateTwitchBadgeInline(TwitchChatBadge badge)
    {
        if (string.IsNullOrWhiteSpace(badge.ImageUrl))
        {
            return CreateBadgeTextFallback(badge);
        }

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
        _ = LoadTwitchBadgeImageAsync(inline, image, badge);
        return inline;
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
            FontSize = 8,
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
        var terms = ParseTerms(_settings.HighlightTerms);
        foreach (var emote in row.Emotes)
        {
            if (emote.Start < position || emote.End >= text.Length)
            {
                continue;
            }

            AddHighlightedText(textBlock, text[position..emote.Start], terms);
            var fallbackText = text[emote.Start..(emote.End + 1)];
            var image = new Image
            {
                Source = GetTwitchEmoteImage(emote.Id),
                Width = Math.Max(18, ViewModel.FontSize * 1.6),
                Height = Math.Max(18, ViewModel.FontSize * 1.6),
                Stretch = Stretch.Uniform,
                ToolTip = fallbackText,
                Margin = new Thickness(1, 0, 1, 0)
            };
            image.ImageFailed += (_, _) =>
            {
                if (image.Parent is InlineUIContainer inline)
                {
                    inline.Child = new TextBlock
                    {
                        Text = fallbackText,
                        FontSize = ViewModel.FontSize,
                        Foreground = new SolidColorBrush(GetChatTextColor())
                    };
                }

                ViewModel.UpdateDiagnostic($"Não foi possível carregar o emote da Twitch: {fallbackText}.");
            };
            textBlock.Inlines.Add(new InlineUIContainer(image) { BaselineAlignment = BaselineAlignment.Center });
            position = emote.End + 1;
        }

        AddHighlightedText(textBlock, text[position..], terms);
    }

    private ImageSource GetTwitchEmoteImage(string emoteId)
    {
        var imageKey = emoteId;
        if (_twitchEmoteImageCache.TryGetValue(imageKey, out var cachedImage))
        {
            return cachedImage;
        }

        var uri = new Uri($"https://static-cdn.jtvnw.net/emoticons/v2/{Uri.EscapeDataString(emoteId)}/default/dark/2.0");
        var image = new BitmapImage();
        image.BeginInit();
        image.UriSource = uri;
        image.CacheOption = BitmapCacheOption.OnDemand;
        image.EndInit();
        _twitchEmoteImageCache[imageKey] = image;
        return image;
    }

    private async Task LoadTwitchBadgeImageAsync(
        InlineUIContainer inline,
        Image image,
        TwitchChatBadge badge)
    {
        try
        {
            if (!_twitchBadgeImageCache.TryGetValue(badge.ImageUrl, out var imageTask))
            {
                imageTask = DownloadTwitchBadgeImageAsync(badge.ImageUrl);
                _twitchBadgeImageCache[badge.ImageUrl] = imageTask;
            }

            image.Source = await imageTask;
            inline.Child = image;
        }
        catch (Exception ex) when (
            ex is HttpRequestException or IOException or InvalidDataException or NotSupportedException or
                TaskCanceledException or FormatException or ArgumentException or InvalidOperationException)
        {
            _twitchBadgeImageCache.Remove(badge.ImageUrl);
            ViewModel.UpdateDiagnostic($"Não foi possível carregar a imagem do badge Twitch “{badge.Title}”: {ex.Message}");
        }
    }

    private static async Task<ImageSource> DownloadTwitchBadgeImageAsync(string imageUrl)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, imageUrl);
                request.Headers.UserAgent.ParseAdd("TransparentChatFilter/1.0");
                using var response = await TwitchBadgeImageHttpClient.SendAsync(request);
                response.EnsureSuccessStatusCode();
                var imageBytes = await response.Content.ReadAsByteArrayAsync();
                using var stream = new MemoryStream(imageBytes);
                var image = new BitmapImage();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.StreamSource = stream;
                image.EndInit();
                image.Freeze();
                return image;
            }
            catch (Exception ex) when (
                attempt == 0 &&
                (ex is HttpRequestException or IOException or InvalidDataException or TaskCanceledException))
            {
                await Task.Delay(TimeSpan.FromMilliseconds(350));
            }
        }
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
            if (!_twitchChannelImageCache.TryGetValue(imageUrl, out var imageTask))
            {
                imageTask = DownloadTwitchBadgeImageAsync(imageUrl);
                _twitchChannelImageCache[imageUrl] = imageTask;
            }

            image.Source = await imageTask;
        }
        catch (Exception ex) when (
            ex is HttpRequestException or IOException or InvalidDataException or NotSupportedException or
                TaskCanceledException or FormatException or ArgumentException or InvalidOperationException)
        {
            _twitchChannelImageCache.Remove(imageUrl);
            inline.Child = null;
            ViewModel.UpdateDiagnostic($"Não foi possível carregar a foto do canal Twitch: {ex.Message}");
        }
    }

    private void AddHighlightedText(TextBlock textBlock, string text, IReadOnlyList<string> terms)
    {
        if (text.Length == 0)
        {
            return;
        }

        var accent = ParseHexColor(_settings.AccentColor) ?? Color.FromRgb(94, 162, 255);
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
                    Foreground = new SolidColorBrush(GetChatTextColor())
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
                Foreground = new SolidColorBrush(GetChatTextColor())
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

    private static Color? TryParseColor(string rawMetadata)
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
