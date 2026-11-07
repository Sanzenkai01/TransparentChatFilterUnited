using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace TransparentChatFilter;

public partial class App : Application
{
    private static readonly TimeSpan MinimumUpdateCheckInterval = TimeSpan.FromHours(6);
    private readonly DispatcherTimer _updateCheckTimer = new()
    {
        Interval = MinimumUpdateCheckInterval
    };
    private bool _updateCheckInProgress;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var settingsFileExisted = File.Exists(new AppSettings().SettingsPath);
        var mainWindow = new MainWindow();
        MainWindow = mainWindow;
        var currentVersion = typeof(App).Assembly.GetName().Version
            ?? throw new InvalidOperationException("Não foi possível determinar a versão instalada.");
        var settings = mainWindow.ViewModel.Settings;
        var lastSeenVersion = Version.TryParse(settings.LastSeenReleaseNotesVersion, out var parsedVersion)
            ? parsedVersion
            : null;
        var pendingUpdateVersion = ReadPendingReleaseNotesVersion(mainWindow);
        var releaseVersion = NormalizeReleaseVersion(currentVersion);
        var hasUnseenReleaseNotes = lastSeenVersion is null ||
                                    NormalizeReleaseVersion(lastSeenVersion) < releaseVersion;
        var pendingUpdate = pendingUpdateVersion is not null &&
                            NormalizeReleaseVersion(pendingUpdateVersion) == releaseVersion &&
                            hasUnseenReleaseNotes;
        var updatedFromLegacyInstaller = lastSeenVersion is not null &&
                                         NormalizeReleaseVersion(lastSeenVersion) < releaseVersion;
        var upgradedFromUntrackedLegacyVersion = settingsFileExisted && lastSeenVersion is null;
        var showReleaseNotes = pendingUpdate || updatedFromLegacyInstaller || upgradedFromUntrackedLegacyVersion;
        TaskCompletionSource? notesDismissed = null;
        if (showReleaseNotes)
        {
            mainWindow.WindowState = WindowState.Normal;
            notesDismissed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            mainWindow.Loaded += (_, _) =>
            {
                var releaseNotesWindow = new ReleaseNotesWindow(currentVersion, lastSeenVersion)
                {
                    Owner = mainWindow
                };
                try
                {
                    if (releaseNotesWindow.ShowDialog() != true)
                    {
                        return;
                    }

                    settings.LastSeenReleaseNotesVersion = releaseVersion.ToString(3);
                    try
                    {
                        mainWindow.QueueSettingsSaveForExternalChanges();
                        if (pendingUpdate)
                        {
                            File.Delete(AppSettings.PendingReleaseNotesPath);
                        }
                    }
                    catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                    {
                        mainWindow.ViewModel.UpdateDiagnostic(
                            $"Não foi possível registrar que as novidades foram exibidas: {exception.Message}");
                    }
                }
                finally
                {
                    notesDismissed.TrySetResult();
                }
            };
        }

        mainWindow.Show();
        _updateCheckTimer.Tick += (_, _) => _ = CheckForUpdatesAsync(mainWindow);
        mainWindow.ContentRendered += (_, _) =>
        {
            _updateCheckTimer.Start();
            _ = CheckForUpdatesAsync(mainWindow, notesDismissed?.Task);
        };
    }

    private static Version NormalizeReleaseVersion(Version version)
    {
        return new Version(version.Major, version.Minor, Math.Max(version.Build, 0));
    }

    private static Version? ReadPendingReleaseNotesVersion(MainWindow mainWindow)
    {
        try
        {
            if (!File.Exists(AppSettings.PendingReleaseNotesPath))
            {
                return null;
            }

            var versionText = File.ReadAllText(AppSettings.PendingReleaseNotesPath).Trim();
            if (Version.TryParse(versionText, out var version))
            {
                return version;
            }

            mainWindow.ViewModel.UpdateDiagnostic(
                "O marcador da atualização está inválido; as novidades não puderam ser confirmadas.");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            mainWindow.ViewModel.UpdateDiagnostic(
                $"Não foi possível verificar o marcador das novidades: {exception.Message}");
        }

        return null;
    }

    private async Task CheckForUpdatesAsync(MainWindow mainWindow, Task? waitBeforeShowingUpdateUi = null)
    {
        if (_updateCheckInProgress)
        {
            return;
        }

        var settings = mainWindow.ViewModel.Settings;
        var checkStartedAt = DateTimeOffset.UtcNow;
        if (settings.LastUpdateCheckUtc is { } lastCheck &&
            checkStartedAt - lastCheck < MinimumUpdateCheckInterval)
        {
            return;
        }

        _updateCheckInProgress = true;
        UpdateCheckWindow? progressWindow = null;
        var downloading = false;
        try
        {
            var result = await UpdateService.CheckForUpdateAsync(settings.UpdateCheckETag);
            settings.LastUpdateCheckUtc = checkStartedAt;
            settings.UpdateCheckETag = result.ETag ?? settings.UpdateCheckETag;
            mainWindow.QueueSettingsSaveForExternalChanges();

            if (result.RateLimited)
            {
                mainWindow.ViewModel.UpdateDiagnostic(
                    "O GitHub limitou temporariamente as consultas de atualização. Uma nova verificação será feita após pelo menos 6 horas.");
                return;
            }

            if (result.NotModified || result.Update is not { } update)
            {
                return;
            }

            if (string.Equals(
                    settings.LastSkippedUpdateVersion,
                    update.Version.ToString(3),
                    StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (waitBeforeShowingUpdateUi is not null)
            {
                await waitBeforeShowingUpdateUi;
            }

            var updatePrompt = new UpdateAvailableWindow(update)
            {
                Owner = mainWindow
            };
            updatePrompt.ShowDialog();
            if (updatePrompt.Choice == UpdatePromptChoice.SkipVersion)
            {
                settings.LastSkippedUpdateVersion = update.Version.ToString(3);
                mainWindow.QueueSettingsSaveForExternalChanges();
                return;
            }

            if (updatePrompt.Choice != UpdatePromptChoice.Update)
            {
                return;
            }

            downloading = true;
            using var cancellationSource = new CancellationTokenSource();
            progressWindow = new UpdateCheckWindow("Baixando e verificando a atualização...", cancellationSource)
            {
                Owner = mainWindow
            };
            progressWindow.Show();

            var progress = new Progress<double>(progressWindow.ReportProgress);
            var archivePath = await UpdateService.DownloadUpdateAsync(
                update,
                progress,
                cancellationSource.Token);
            UpdateService.StartUpdate(archivePath, update.Version);
            Current.Shutdown();
        }
        catch (OperationCanceledException) when (progressWindow is not null)
        {
            mainWindow.ViewModel.UpdateDiagnostic("O download da atualização foi cancelado.");
        }
        catch (Exception exception)
        {
            mainWindow.ViewModel.UpdateDiagnostic(
                $"Falha ao verificar ou baixar atualizações: {exception.Message}");
            if (downloading)
            {
                MessageBox.Show(
                    $"Não foi possível baixar ou verificar a atualização: {exception.Message}",
                    "Atualização",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }
        finally
        {
            progressWindow?.Close();
            _updateCheckInProgress = false;
        }
    }
}
