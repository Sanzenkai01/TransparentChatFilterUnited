using System.Windows;

namespace TransparentChatFilter;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var mainWindow = new MainWindow();
        MainWindow = mainWindow;
        mainWindow.Show();
        _ = CheckForUpdatesAsync();
    }

    private static async Task CheckForUpdatesAsync()
    {
        try
        {
            var update = await UpdateService.CheckForUpdateAsync();
            if (update is null)
            {
                return;
            }

            var answer = MessageBox.Show(
                $"A versão {update.Version} está disponível. Deseja baixar e instalar agora? O programa será fechado durante a atualização.",
                "Atualização disponível",
                MessageBoxButton.YesNo,
                MessageBoxImage.Information);

            if (answer != MessageBoxResult.Yes)
            {
                return;
            }

            var archivePath = await UpdateService.DownloadUpdateAsync(update);
            UpdateService.StartUpdate(archivePath);
            Current.Shutdown();
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                $"Não foi possível verificar ou baixar atualizações: {exception.Message}",
                "Atualização",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }
}
