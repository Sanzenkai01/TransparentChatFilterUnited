# Arquitetura do TransparentChatFilterUnited

## Visão geral

Aplicativo desktop WPF para Windows (.NET 8). `App.xaml` inicializa a aplicação; `MainWindow` reúne o overlay do chat e suas configurações. A conexão com o chat autenticado, OAuth, consulta de badges/imagens e atualização estão em serviços separados. O chat público é apresentado por WebView2 e integrado à janela principal.

## Arquivos-fonte

Cada linha abaixo descreve um arquivo `.cs` ou `.xaml` mantido no projeto (arquivos gerados em `obj`, `bin` e `publish` não estão incluídos).

| Arquivo | Responsabilidade |
|---|---|
| `App.xaml` | Declara a aplicação WPF e seus recursos globais. |
| `App.xaml.cs` | Inicialização, decisão de exibir novidades da versão e coordenação do fluxo de atualização. |
| `AppSettings.cs` | Modelos de configuração e enums; carrega, valida, migra por versão e recupera preferências JSON de backup, incluindo o contorno do texto. |
| `AtomicFile.cs` | Grava arquivos de texto via temporário e substituição atômica, mantendo cópia `.bak`. |
| `AssemblyInfo.cs` | Metadados WPF para localizar dicionários de recursos de tema. |
| `MainWindow.xaml` | Layout do overlay, feed de mensagens, controles da janela, aparência e páginas de preferências. |
| `MainWindow.xaml.cs` | Eventos e ciclo de vida WPF, preferências, coordenação de conexão/reconexão, feed, filtros, animação de mensagens novas, notificações, renderização e WebView2; detalhes abaixo. |
| `Native/MainWindow.Native.cs` | Parte `partial` da janela que agrupa hotkeys, hit-test de redimensionamento, cursor/arrasto do modo transparente e estilo nativo de clique através. |
| `Native/NativeMethods.cs` | Estruturas, constantes e declarações P/Invoke do Win32 usadas pela janela. |
| `ReleaseNotesWindow.xaml` | Layout do diálogo de novidades da versão. |
| `ReleaseNotesWindow.xaml.cs` | Histórico de novidades, seleção das notas por versão, animação e ações de confirmação/dispensa. |
| `Scripts/ScriptLoader.cs` | Carrega scripts JavaScript incorporados no assembly para uso pelo WebView2. |
| `IrcLineParser.cs` | Faz uma única leitura sintática de cada linha IRC (tags, prefixo, comando e parâmetros), decodifica tags IRCv3 e produz os eventos/mensagens tipados. |
| `TwitchBadgeClient.cs` | Consulta e resolução de badges globais/de canal pela API Helix, com prioridade de canal, cache concorrente limitado e fallback de texto. |
| `BoundedAsyncCache.cs` | Cache assíncrono concorrente com deduplicação de tarefas em andamento, capacidade LRU e expiração curta de falhas. |
| `TwitchChannelClient.cs` | Consulta e cache da imagem de perfil de um canal pela API Helix. |
| `TwitchChatClient.cs` | Conexão TLS/IRC com a Twitch; entrada e saída de canais, despacho de eventos do parser, isolamento/diagnóstico de linhas inválidas e coordenação de reconexão. Também define os modelos de mensagem, badges e eventos usados pelo chat. |
| `TwitchCredentialStore.cs` | Persistência local de credenciais da Twitch protegidas com DPAPI e gravação atômica. |
| `TwitchOAuthClient.cs` | Autorização por código de dispositivo, validação/renovação OAuth e modelos desse fluxo. |
| `UpdateAvailableWindow.xaml` | Layout do diálogo que oferece uma atualização. |
| `UpdateAvailableWindow.xaml.cs` | Apresenta a versão e as notas da release, retornando atualizar, lembrar mais tarde ou pular a versão. |
| `UpdateCheckWindow.xaml` | Layout do progresso cancelável do download de atualização. |
| `UpdateCheckWindow.xaml.cs` | Exibe o progresso do download e cancela via `CancellationTokenSource`. |
| `UpdateService.cs` | Consulta releases com ETag, trata rate limit, baixa com limite de tamanho, confere SHA-256 e inicia a instalação. |
| `WindowsStartupRegistration.cs` | Registra ou remove o executável na chave `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`. |
| `ViewModels/ChatRowView.cs` | Modelo de apresentação de uma linha do chat, incluindo largura, aparência e configurações de renderização por mensagem. |
| `ViewModels/OverlayViewModel.cs` | View model do overlay: estado observável, configurações e conversão de listas editáveis. |

### Scripts incorporados

| Arquivo | Responsabilidade |
|---|---|
| `Scripts/TwitchCaptureScript.js` | Observa somente o contêiner de mensagens, enfileira nós adicionados por `requestAnimationFrame`, reconcilia a lista a cada 10 segundos e serializa texto, emotes e badges para o aplicativo. |
| `Scripts/TwitchMessagesOnlyScript.js` | Define o objeto único de seletores do DOM consumido pelos dois scripts e limita a apresentação do popout à área de mensagens. |

O `.csproj` incorpora esses arquivos como `EmbeddedResource`; `ScriptLoader` os lê pelos nomes lógicos definidos no projeto. Os pontos de registro e execução no WebView2 continuam na janela principal.

### Captura pública e custo do WebView2

O script de captura procura o contêiner por polling curto até ele existir e, depois, observa somente esse contêiner. As mutações processam `addedNodes`; a reconciliação a cada 10 segundos recupera alterações de conteúdo feitas sem inclusão de nós e substituições do próprio contêiner. O `WeakMap` evita reenviar linhas sem alterações. A configuração de seletores em `TwitchMessagesOnlyScript.js` é compartilhada com o capturador via `window.__tcfTwitchDomConfig`.

O capturador serializa o corpo percorrendo `childNodes`: texto mantém a ordem e imagens de emote viram texto alternativo mais intervalos de ID/posição, que o code-behind valida antes de criar `TwitchEmoteRange`. Os offsets JavaScript e as strings .NET usam UTF-16. Badges são classificados por metadados/IDs de badge e segmentos semânticos da URL, nunca pelo texto localizado do `alt`; se só houver um ID opaco do CDN, a badge ainda pode ser exibida, mas não recebe uma classificação de moderador/VIP/inscrito.

No WebView2, DevTools, menus de contexto e autofill estão desativados. `WebResourceRequested` responde 204 para recursos de mídia nos endpoints de vídeo da Twitch e para hosts conhecidos de anúncio/telemetria. O filtro evita bloquear `gql.twitch.tv`, que pode atender recursos essenciais da página/chat; portanto, não é um bloqueador genérico de todo anúncio servido por endpoints compartilhados. Quando a janela está inativa ou minimizada, `MemoryUsageTargetLevel` passa para `Low`; ao voltar ao primeiro plano retorna a `Normal`, sem suspender os scripts que integram as mensagens.

Para comparar memória antes/depois, mantenha o app conectado ao mesmo canal e nas mesmas condições por cinco minutos em cada execução. Rode `powershell -ExecutionPolicy Bypass -File .\ferramentas\medir.ps1`; o script grava 60 amostras a cada 5 segundos em `ferramentas\baseline.csv` e soma a memória de trabalho e CPU dos processos `msedgewebview2` observados. O arquivo é acrescentado, com um `run_id` por execução. Para comparação válida, feche outras janelas/processos Edge WebView2 e compare a média (ou mediana) por `run_id`, mantendo iguais canal, janela, modo de conexão e atividade do chat.

### Avaliação: substituir o popout por IRC sem autenticação

Esta alternativa foi avaliada, mas não implementada. Ela eliminaria o WebView2, seus processos Edge e a dependência de seletores de DOM, diminuindo o consumo e removendo scraping. Também exigiria manter o parser IRC e lidar com limitações/particularidades do protocolo.

**O login anônimo `justinfanNNNN` não é um modo documentado/suportado pela documentação atual da Twitch (consultada em 2026-11-07).** A documentação atual de IRC diz que é necessário autenticar e obter um User Access Token com `chat:read` para ler chat; a página de autenticação também diz que usuários de Installed Chatbots e Chat Clients precisam autenticar. A convenção `justinfan` aparece em exemplos históricos/comunitários, mas não deve ser assumida como um contrato compatível.

Sem token, as tags IRC podem carregar IDs/versões de badges e os intervalos/IDs de emotes, mas não fornecem por si só o catálogo de imagens das badges. Os endpoints Helix para badges requerem token de acesso; sem credencial, as badges teriam de ser omitidas ou mostradas como texto, a menos que o app mantivesse um mapeamento externo próprio. Emotes são mais viáveis: as tags `emotes` do IRC incluem IDs e posições e a CDN da Twitch permite montar as imagens, embora seja preciso validar o formato/offsets e aceitar que o IRC tem cobertura de recursos mais limitada que a interface web. Se uma versão futura substituir o WebView2 por IRC, a opção segura/documentada é autenticar o usuário com o fluxo OAuth já existente, não depender de `justinfan`.

Referências oficiais verificadas:
- [Twitch IRC Concepts](https://dev.twitch.tv/docs/chat/irc/) — autenticação, tags e limitações do IRC.
- [Twitch Chat Authentication](https://dev.twitch.tv/docs/chat/authenticating/) — requisitos de autenticação para chat clients e installed chatbots.
- [Twitch API Reference](https://dev.twitch.tv/docs/api/reference/) e [Getting OAuth Access Tokens](https://dev.twitch.tv/docs/authentication/getting-tokens-oauth/) — APIs Helix e tokens.

## Responsabilidades concentradas em `MainWindow.xaml.cs`

O arquivo tem atualmente **2.536 linhas**. Ele ainda combina responsabilidades de apresentação, estado e integração. Entre as principais:

- **Janela e preferências**: inicialização/fechamento, posição e tamanho persistidos, modo maximizado, páginas de configuração e salvamento.
- **Coordenação de conexão**: alternância entre chat público/IRC autenticado, autorização, reconexão, canais conectados e atualização do estado visual.
- **Recepção e preparação de mensagens**: integração de mensagens do WebView2 e do `TwitchChatClient`, resolução de badges e imagens de canal.
- **Regras do feed**: filtragem por canal/usuário/termos, tratamento de mensagens removidas, retenção do histórico e atualização/rolagem da lista.
- **Renderização WPF**: construção das linhas, inlines de badges/emotes/imagens, destaque de termos e cores.
- **Cache de imagens**: cache LRU limitado de emotes, badges e fotos de canais; downloads são canceláveis, têm timeout e decodificam/freezam bitmaps para o DPI de destino. GIFs de emotes preservam todos os quadros e são animados pela propriedade `Image.Source` do WPF; a animação respeita `SystemParameters.ClientAreaAnimation`. No IRC autenticado, os limites caem de 512/256/128 para 128/64/32 itens, liberando imediatamente as entradas menos usadas; ao voltar ao chat público, os limites originais são restaurados. Imagens ainda referenciadas por mensagens visíveis não são removidas.
- **Notificações**: geração e reprodução dos sons de notificação e controle de cooldown.
- **Chat público/WebView2**: ciclo de vida do popout, navegação e coordenação da execução dos scripts incorporados.
- **Preferências e comandos de interface**: navegação pelas páginas de configurações, salvamento, seleção de canais, cores, opacidade, tamanho de fonte e controles do modo somente chat.

### Divisão futura proposta — não implementada

Blocos coesos que podem ser extraídos em outra etapa, mantendo a janela como adaptador de UI:

1. **`MessageFilterService`**: concentrar a lógica pura de `ShouldDisplayMessage`, `ContainsAnyTerm` e `ParseTerms`, recebendo a mensagem e as configurações. A janela continuaria decidindo o aviso visual e a inclusão/remoção das linhas.
2. **`ChatImageCache`**: concentrar os caches de emotes, badges e imagens de canal, a deduplicação de downloads em andamento e o carregamento/decodificação de `ImageSource`. A exibição de erros e a associação das imagens aos elementos WPF permaneceriam na camada de apresentação.
3. **`ChatReconnectService`**: concentrar o agendamento único, o atraso de reconexão e a política baseada em `AutoReconnect`/estado de conexão. A chamada que atualiza a UI e inicia `ConnectAsync` continuaria coordenada pela janela ou por um serviço de conexão em etapa futura.
4. **`ChatConnectionService` (opcional, etapa posterior)**: se a extração de reconexão revelar acoplamento excessivo, reunir seleção de modo, autorização e ciclo de conexão IRC, sem incluir controles WPF.

Os clientes Twitch, OAuth, credenciais e atualização já estão em arquivos separados. A lista propõe somente as responsabilidades ainda concentradas no code-behind; nenhuma divisão de serviços foi implementada nesta etapa.

## Verificação do build

`dotnet build TransparentChatFilter.csproj -c Release` terminou com êxito: **0 avisos e 0 erros**. Os testes ficam em `Tests/` e são executados com `dotnet test Tests\TransparentChatFilter.Tests.csproj -c Release`; última execução: **32 aprovados, 0 falhas**.
