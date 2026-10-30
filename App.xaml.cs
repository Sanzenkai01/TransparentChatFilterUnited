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
        UpdateCheckWindow? progressWindow = null;

        try
        {
            progressWindow = new UpdateCheckWindow("Checando atualizações...")
            {
                Owner = Current.MainWindow
            };
            progressWindow.Show();

            var updateTask = UpdateService.CheckForUpdateAsync();
            await Task.WhenAll(updateTask, Task.Delay(TimeSpan.FromMilliseconds(1500)));
            var update = await updateTask;
            progressWindow.Close();
            progressWindow = null;

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

            progressWindow = new UpdateCheckWindow("Baixando atualização...")
            {
                Owner = Current.MainWindow
            };
            progressWindow.Show();

            var archivePath = await UpdateService.DownloadUpdateAsync(update);
            UpdateService.StartUpdate(archivePath);
            Current.Shutdown();
        }
        catch (Exception exception)
        {
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
