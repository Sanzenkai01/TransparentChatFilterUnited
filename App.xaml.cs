using System.IO;
using System.Windows;

namespace TransparentChatFilter;

public partial class App : Application
{
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
        var updateCheckStarted = false;
        if (pendingUpdate || updatedFromLegacyInstaller || upgradedFromUntrackedLegacyVersion)
        {
            mainWindow.WindowState = WindowState.Normal;
            mainWindow.Loaded += (_, _) =>
            {
                var releaseNotesWindow = new ReleaseNotesWindow(currentVersion, lastSeenVersion)
                {
                    Owner = mainWindow
                };
                var notesDismissed = new TaskCompletionSource(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                updateCheckStarted = true;
                _ = CheckForUpdatesAsync(notesDismissed.Task);
                try
                {
                    if (releaseNotesWindow.ShowDialog() != true)
                    {
                        return;
                    }

                    settings.LastSeenReleaseNotesVersion = releaseVersion.ToString(3);
                    try
                    {
                        mainWindow.ViewModel.SaveSettings();
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
        if (!updateCheckStarted)
        {
            _ = CheckForUpdatesAsync();
        }
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

    private static async Task CheckForUpdatesAsync(Task? waitBeforeShowingUpdateUi = null)
    {
        UpdateCheckWindow? progressWindow = null;

        try
        {
            if (waitBeforeShowingUpdateUi is null)
            {
                progressWindow = new UpdateCheckWindow("Checando atualizações...")
                {
                    Owner = Current.MainWindow
                };
                progressWindow.Show();
            }

            var updateTask = UpdateService.CheckForUpdateAsync();
            await Task.WhenAll(updateTask, Task.Delay(TimeSpan.FromMilliseconds(1500)));
            var update = await updateTask;
            progressWindow?.Close();
            progressWindow = null;

            if (update is null)
            {
                return;
            }

            if (waitBeforeShowingUpdateUi is not null)
            {
                await waitBeforeShowingUpdateUi;
            }

            var updatePrompt = new UpdateAvailableWindow(update.Version)
            {
                Owner = Current.MainWindow
            };
            if (updatePrompt.ShowDialog() != true)
            {
                return;
            }

            progressWindow = new UpdateCheckWindow("Baixando atualização...")
            {
                Owner = Current.MainWindow
            };
            progressWindow.Show();

            var archivePath = await UpdateService.DownloadUpdateAsync(update);
            UpdateService.StartUpdate(archivePath, update.Version);
            Current.Shutdown();
        }
        catch (Exception exception)
        {
            if (waitBeforeShowingUpdateUi is not null)
            {
                await waitBeforeShowingUpdateUi;
            }

            progressWindow?.Close();
            progressWindow = null;
            MessageBox.Show(
                $"Não foi possível verificar ou baixar atualizações: {exception.Message}",
                "Atualização",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        finally
        {
            progressWindow?.Close();
        }
    }
}
