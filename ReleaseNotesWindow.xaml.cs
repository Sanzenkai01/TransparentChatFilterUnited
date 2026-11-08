using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace TransparentChatFilter;

public partial class ReleaseNotesWindow : Window
{
    private static readonly ReleaseNote[] ReleaseHistory =
    [
        new(new Version(1, 1, 3), "Controles de chat e conexão aprimorados; opções de canais adaptadas ao modo de filtro; ajustes na cor do fundo, badges e apresentação das mensagens; IRC somente para leitura; títulos e descrições das configurações centralizados."),
        new(new Version(1, 1, 4), "Corrigida a solicitação de autorização IRC após desconexões e adicionada uma animação ao conectar."),
        new(new Version(1, 1, 5), "Adicionado um controle para ajustar a opacidade do fundo das mensagens."),
        new(new Version(1, 1, 6), "Corrigidos o valor padrão da opacidade do fundo das mensagens e seu comportamento quando desativada."),
        new(new Version(1, 1, 7), "Adicionado o popup de novidades após a atualização do aplicativo."),
        new(new Version(1, 1, 8), "Corrigida a exibição das notas de atualização para instalações legadas."),
        new(new Version(1, 1, 9), "Notas de atualização exibidas somente após uma atualização e apenas uma vez por versão após a confirmação."),
        new(new Version(1, 2, 0), "Textos do popup de novidades centralizados e uso de memória otimizado ao descartar o WebView2 ao mudar do chat público para o IRC."),
        new(new Version(1, 2, 1), "Configurações e atualizações mais confiáveis, interface reorganizada, animação de emotes GIF e menor retenção de imagens no modo IRC."),
        new(new Version(1, 2, 2), "Ajustes no espaçamento e no limite de mensagens, alertas configuráveis para canais adicionais e novo ícone do aplicativo."),
        new(new Version(1, 2, 3), "Verificação de atualizações em segundo plano uma vez a cada abertura, sem intervalo mínimo entre inicializações.")
    ];

    public ReleaseNotesWindow(Version version, Version? lastSeenVersion, string? changelog = null)
    {
        InitializeComponent();
        Title = $"Novidades da versão {version.ToString(3)}";
        VersionTitle.Text = $"Versão {version.ToString(3)}";
        if (!string.IsNullOrWhiteSpace(changelog))
        {
            ReleaseNotesIntro.Text = "Changelog completo desta atualização:";
            ReleaseNotesItems.ItemsSource =
                new[] { new ReleaseNoteItem(version.ToString(3), changelog) };
            return;
        }

        var previousVersion = lastSeenVersion is null
            ? new Version(0, 0)
            : NormalizeVersion(lastSeenVersion);
        ReleaseNotesIntro.Text = lastSeenVersion is null
            ? "Confira as melhorias acumuladas:"
            : $"Novidades desde a versão {lastSeenVersion.ToString(3)}:";
        ReleaseNotesItems.ItemsSource = ReleaseHistory
            .Where(note => note.Version > previousVersion && note.Version <= NormalizeVersion(version))
            .Select(note => new ReleaseNoteItem(note.Version.ToString(3), note.Description))
            .ToArray();
    }

    private static Version NormalizeVersion(Version version)
    {
        return new Version(version.Major, version.Minor, Math.Max(version.Build, 0));
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            DragMove();
        }
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        if (!SystemParameters.ClientAreaAnimation)
        {
            DialogBorder.Opacity = 1;
            return;
        }

        DialogBorder.Opacity = 0;
        DialogBorder.BeginAnimation(
            OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(160))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            });
    }

    private void DismissButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void AcknowledgeButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }

    private sealed record ReleaseNote(Version Version, string Description);

    private sealed record ReleaseNoteItem(string Version, string Description);
}
