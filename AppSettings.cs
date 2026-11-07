using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TransparentChatFilter;

public enum ChatFilterMode
{
    OnlyMyChannel,
    JoinedChats
}

public enum ChatConnectionMode
{
    PublicPopout,
    AuthenticatedIrc
}

public enum MessageNotificationSound
{
    Neutral,
    Soft,
    Bell
}

public sealed class AppSettings
{
    private const int CurrentSettingsVersion = 9;

    public string MainChannel { get; set; } = string.Empty;
    public string TwitchUsername { get; set; } = string.Empty;
    public double? WindowLeft { get; set; }
    public double? WindowTop { get; set; }
    public double? WindowWidth { get; set; }
    public double? WindowHeight { get; set; }
    public ChatConnectionMode ConnectionMode { get; set; } = ChatConnectionMode.PublicPopout;
    public ChatFilterMode FilterMode { get; set; } = ChatFilterMode.OnlyMyChannel;
    public List<string> SelectedChannels { get; set; } = new();
    public List<string> BlockedChannels { get; set; } = new();
    public List<string> BlockedUsers { get; set; } = new();
    public List<string> HighlightedUsers { get; set; } = new();
    public string BlockedTerms { get; set; } = string.Empty;
    public string HighlightTerms { get; set; } = string.Empty;
    public bool HideCommands { get; set; }
    public bool ShowTimestamps { get; set; } = true;
    public bool ShowBadges { get; set; } = true;
    public bool ShowUserNames { get; set; } = true;
    public bool UserNameAboveMessage { get; set; }
    public bool UseTwitchNameColors { get; set; } = true;
    public bool ShowModerators { get; set; } = true;
    public bool ShowVips { get; set; } = true;
    public bool DimOtherChannelMessages { get; set; }
    public bool EnableSound { get; set; } = false;
    public bool NotifyNewMessages { get; set; } = true;
    public bool NotifyOtherChannelMessages { get; set; } = true;
    public int SettingsVersion { get; set; }
    public string LastSeenReleaseNotesVersion { get; set; } = string.Empty;
    public MessageNotificationSound NotificationSound { get; set; } = MessageNotificationSound.Neutral;
    public int NotificationVolumePercent { get; set; } = 50;
    public bool AutoReconnect { get; set; } = true;
    public bool StartMinimized { get; set; } = false;
    public bool StartTransparentAndConnected { get; set; }
    public bool StartWithWindows { get; set; } = false;
    public bool HideSettingsDuringUse { get; set; } = false;
    public bool ClickThroughEnabled { get; set; } = false;
    public bool IsTransparent { get; set; } = false;
    public bool ShowSettingsPanel { get; set; } = true;
    public double BackgroundOpacity { get; set; } = 0.86;
    public double TextOpacity { get; set; } = 1.0;
    public bool ShowChatTextOutline { get; set; }
    public bool UseMessageBackgroundOpacity { get; set; } = true;
    public double MessageBackgroundOpacity { get; set; } = 0.75;
    public int FontSize { get; set; } = 14;
    public int MessageSpacing { get; set; } = 6;
    public int MaxVisibleMessages { get; set; } = 20;
    public string HighlightColor { get; set; } = RgbHex(94, 162, 255);
    public string MessageBackgroundColor { get; set; } = RgbHex(39, 43, 47);
    public string[] RecentChannels { get; set; } = Array.Empty<string>();
    public DateTimeOffset? LastUpdateCheckUtc { get; set; }
    public string UpdateCheckETag { get; set; } = string.Empty;
    public string LastSkippedUpdateVersion { get; set; } = string.Empty;

    [JsonIgnore]
    public string SettingsPath { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "TransparentChatFilter",
        "settings.json");

    [JsonIgnore]
    public string? LoadWarning { get; private set; }

    internal static string PendingReleaseNotesPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "TransparentChatFilter",
        "pending-release-notes.txt");

    public static AppSettings Load(string? path = null)
    {
        var targetPath = path ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "TransparentChatFilter",
            "settings.json");

        var backupPath = targetPath + ".bak";
        if (!File.Exists(targetPath))
        {
            if (File.Exists(backupPath))
            {
                try
                {
                    var recovered = LoadFromFile(backupPath, targetPath);
                    recovered.LoadWarning = "As configurações principais não foram encontradas; uma cópia de segurança foi carregada.";
                    return recovered;
                }
                catch (Exception ex) when (IsSettingsLoadFailure(ex))
                {
                    return CreateDefaults(targetPath, $"Não foi possível ler a cópia de segurança das configurações: {ex.Message}");
                }
            }

            return new AppSettings
            {
                SettingsPath = targetPath,
                SettingsVersion = CurrentSettingsVersion
            };
        }

        try
        {
            return LoadFromFile(targetPath, targetPath);
        }
        catch (Exception primaryException) when (IsSettingsLoadFailure(primaryException))
        {
            var backupWarning = string.Empty;
            AppSettings? recoveredSettings = null;
            if (File.Exists(backupPath))
            {
                try
                {
                    recoveredSettings = LoadFromFile(backupPath, targetPath);
                }
                catch (Exception backupException) when (IsSettingsLoadFailure(backupException))
                {
                    backupWarning = $" A cópia de segurança também não pôde ser lida: {backupException.Message}";
                }
            }

            var corruptionWarning = PreserveCorruptFile(targetPath);
            if (recoveredSettings is not null)
            {
                recoveredSettings.LoadWarning =
                    $"As configurações estavam inválidas e foram recuperadas da cópia de segurança. {corruptionWarning}";
                return recoveredSettings;
            }

            return CreateDefaults(
                targetPath,
                $"As configurações estavam inválidas e foram substituídas pelos valores padrão. {corruptionWarning}{backupWarning}");
        }
    }

    public void Save()
    {
        SettingsVersion = CurrentSettingsVersion;
        var json = JsonSerializer.Serialize(this, new JsonSerializerOptions
        {
            WriteIndented = true
        });

        AtomicFile.WriteAllText(SettingsPath, json);
    }

    private static AppSettings LoadFromFile(string sourcePath, string targetPath)
    {
        var json = File.ReadAllText(sourcePath);
        var settings = JsonSerializer.Deserialize<AppSettings>(json)
            ?? throw new JsonException("O arquivo não contém um objeto de configurações.");
        using var document = JsonDocument.Parse(json);
        var hasLegacyAccentColor = document.RootElement.TryGetProperty("AccentColor", out var legacyAccentColor)
            && legacyAccentColor.ValueKind == JsonValueKind.String;

        settings.SettingsPath = targetPath;
        if (settings.SettingsVersion < 2)
        {
            settings.NotifyNewMessages = true;
            settings.UseMessageBackgroundOpacity = true;
        }

        if (settings.SettingsVersion < 3)
        {
            if (Math.Abs(settings.BackgroundOpacity - 0.55) < 0.001)
            {
                settings.BackgroundOpacity = 0.86;
            }
        }

        if (settings.SettingsVersion < 4)
        {
            settings.HighlightColor = hasLegacyAccentColor
                ? legacyAccentColor.GetString() ?? RgbHex(94, 162, 255)
                : RgbHex(94, 162, 255);
        }

        if (settings.SettingsVersion < 6)
        {
            if (string.Equals(settings.MessageBackgroundColor, RgbHex(22, 29, 44), StringComparison.OrdinalIgnoreCase))
            {
                settings.MessageBackgroundColor = RgbHex(39, 43, 47);
            }
        }

        if (settings.SettingsVersion < 8)
        {
            settings.ShowChatTextOutline = false;
        }

        if (settings.SettingsVersion < 9 && settings.MaxVisibleMessages == 80)
        {
            settings.MaxVisibleMessages = 20;
        }

        settings.SettingsVersion = CurrentSettingsVersion;
        if (!Enum.IsDefined(settings.FilterMode))
        {
            settings.FilterMode = ChatFilterMode.JoinedChats;
        }

        if (!Enum.IsDefined(settings.ConnectionMode))
        {
            settings.ConnectionMode = ChatConnectionMode.PublicPopout;
        }

        if (!Enum.IsDefined(settings.NotificationSound))
        {
            settings.NotificationSound = MessageNotificationSound.Neutral;
        }

        settings.NotificationVolumePercent = Math.Clamp(settings.NotificationVolumePercent, 0, 100);
        settings.TextOpacity = Math.Clamp(settings.TextOpacity, 0.2, 1.0);
        settings.MessageBackgroundOpacity = Math.Clamp(settings.MessageBackgroundOpacity, 0.1, 1.0);
        settings.MessageSpacing = Math.Clamp(settings.MessageSpacing, 0, 20);
        settings.MaxVisibleMessages = Math.Clamp(settings.MaxVisibleMessages, 10, 300);
        settings.BlockedTerms ??= string.Empty;
        settings.HighlightTerms ??= string.Empty;
        settings.HighlightColor ??= RgbHex(94, 162, 255);
        settings.UpdateCheckETag ??= string.Empty;
        settings.LastSkippedUpdateVersion ??= string.Empty;
        settings.HighlightedUsers ??= new();
        return settings;
    }

    private static AppSettings CreateDefaults(string targetPath, string warning) =>
        new()
        {
            SettingsPath = targetPath,
            SettingsVersion = CurrentSettingsVersion,
            LoadWarning = warning
        };

    private static bool IsSettingsLoadFailure(Exception exception) =>
        exception is JsonException or IOException or UnauthorizedAccessException or NotSupportedException;

    private static string PreserveCorruptFile(string path)
    {
        if (!File.Exists(path))
        {
            return "O arquivo inválido não estava mais disponível para arquivamento.";
        }

        var directory = Path.GetDirectoryName(path) ?? string.Empty;
        var name = Path.GetFileNameWithoutExtension(path);
        var extension = Path.GetExtension(path);
        var timestamp = DateTimeOffset.Now.ToString("yyyyMMdd-HHmmssfff", System.Globalization.CultureInfo.InvariantCulture);
        var corruptPath = Path.Combine(directory, $"{name}.corrupt-{timestamp}{extension}");
        try
        {
            File.Move(path, corruptPath);
            return $"O arquivo inválido foi preservado como {Path.GetFileName(corruptPath)}.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return $"Não foi possível preservar o arquivo inválido: {ex.Message}";
        }

    }

    private static string RgbHex(byte red, byte green, byte blue) =>
        $"#{red:X2}{green:X2}{blue:X2}";
}
