using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Media;

namespace TransparentChatFilter;

public sealed class OverlayViewModel : INotifyPropertyChanged
{
    private readonly AppSettings _settings;

    public OverlayViewModel(AppSettings settings)
    {
        _settings = settings;
        Messages = new ObservableCollection<ChatRowView>();
        SelectedChannels = new ObservableCollection<string>(settings.SelectedChannels ?? []);
        BlockedChannels = new ObservableCollection<string>(settings.BlockedChannels ?? []);
        BlockedUsers = new ObservableCollection<string>(settings.BlockedUsers ?? []);
        BlockedTerms = CreateTermsCollection(settings.BlockedTerms);
        HighlightTerms = CreateTermsCollection(settings.HighlightTerms);
        SelectedChannels.CollectionChanged += (_, _) => settings.SelectedChannels = SelectedChannels.ToList();
        BlockedChannels.CollectionChanged += (_, _) => settings.BlockedChannels = BlockedChannels.ToList();
        BlockedUsers.CollectionChanged += (_, _) => settings.BlockedUsers = BlockedUsers.ToList();
        BlockedTerms.CollectionChanged += (_, _) => settings.BlockedTerms = string.Join(", ", BlockedTerms);
        HighlightTerms.CollectionChanged += (_, _) => settings.HighlightTerms = string.Join(", ", HighlightTerms);
        RecentChannels = new ObservableCollection<string>(
            settings.RecentChannels ?? Array.Empty<string>());
        MainChannel = settings.MainChannel;
        ConnectionStatus = "Desconectado";
        SelectedChannelsText = string.Join(", ", settings.SelectedChannels ?? []);
        BlockedChannelsText = string.Join(", ", settings.BlockedChannels ?? []);
        BlockedUsersText = string.Join(", ", settings.BlockedUsers ?? []);
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
    public ObservableCollection<string> SelectedChannels { get; }
    public ObservableCollection<string> BlockedChannels { get; }
    public ObservableCollection<string> BlockedUsers { get; }
    public ObservableCollection<string> BlockedTerms { get; }
    public ObservableCollection<string> HighlightTerms { get; }

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

    public void RefreshSettingsBindings(params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            OnPropertyChanged(propertyName);
        }

        if (propertyNames.Contains(nameof(ConnectionMode), StringComparer.Ordinal))
        {
            OnPropertyChanged(nameof(IsAuthenticatedIrc));
        }
    }

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
            OnPropertyChanged();
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

    public bool NotifyOtherChannelMessages
    {
        get => _settings.NotifyOtherChannelMessages;
        set
        {
            if (_settings.NotifyOtherChannelMessages == value)
            {
                return;
            }

            _settings.NotifyOtherChannelMessages = value;
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

    public bool ShowChatTextOutline
    {
        get => _settings.ShowChatTextOutline;
        set
        {
            if (_settings.ShowChatTextOutline == value)
            {
                return;
            }

            _settings.ShowChatTextOutline = value;
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

    public string HighlightColor
    {
        get => _settings.HighlightColor;
        set
        {
            if (!IsValidColor(value))
            {
                return;
            }

            _settings.HighlightColor = value;
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
            OnPropertyChanged(nameof(ConnectButtonText));
        }
    }

    public string ConnectButtonText => IsConnecting ? "Conectando..." : IsTwitchConnected ? "Desconectar" : "Conectar";

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

    public bool UserNameAboveMessage
    {
        get => _settings.UserNameAboveMessage;
        set
        {
            if (_settings.UserNameAboveMessage == value)
            {
                return;
            }

            _settings.UserNameAboveMessage = value;
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

    public bool StartTransparentAndConnected
    {
        get => _settings.StartTransparentAndConnected;
        set
        {
            if (_settings.StartTransparentAndConnected == value)
            {
                return;
            }

            _settings.StartTransparentAndConnected = value;
            OnPropertyChanged();
        }
    }

    public bool AutoReconnect
    {
        get => _settings.AutoReconnect;
        set
        {
            if (_settings.AutoReconnect == value)
            {
                return;
            }

            _settings.AutoReconnect = value;
            OnPropertyChanged();
        }
    }

    public bool StartWithWindows
    {
        get => _settings.StartWithWindows;
        set
        {
            if (_settings.StartWithWindows == value)
            {
                return;
            }

            _settings.StartWithWindows = value;
            OnPropertyChanged();
        }
    }

    public bool HideSettingsDuringUse
    {
        get => _settings.HideSettingsDuringUse;
        set
        {
            if (_settings.HideSettingsDuringUse == value)
            {
                return;
            }

            _settings.HideSettingsDuringUse = value;
            OnPropertyChanged();
        }
    }

    public int MaxVisibleMessages
    {
        get => Math.Clamp(_settings.MaxVisibleMessages, 10, 300);
        set
        {
            var maximum = Math.Clamp(value, 10, 300);
            if (_settings.MaxVisibleMessages == maximum)
            {
                return;
            }

            _settings.MaxVisibleMessages = maximum;
            while (Messages.Count > maximum)
            {
                Messages.RemoveAt(0);
            }

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

    private static ObservableCollection<string> CreateTermsCollection(string? value) =>
        new((value ?? string.Empty)
            .Split([',', ';', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase));

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
