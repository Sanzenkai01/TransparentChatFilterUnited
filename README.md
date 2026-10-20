# Transparent Chat Filter

Aplicativo desktop em Windows para exibir um overlay de chat do Twitch com filtro e organização de mensagens em tempo real.

## O que ele faz

- Mostra mensagens de canais do Twitch em um painel transparente
- Permite filtrar por canal, usuário e termos bloqueados
- Suporta exibição de badges, horários, nomes e destaque de mensagens
- Permite ocultar comandos, reduzir ruído e manter o chat em segundo plano
- Funciona como uma janela flutuante "on-top" para acompanhar transmissões sem interferir muito na área de visualização

## Requisitos

- Windows 10 ou 11
- .NET 8 SDK
- Visual Studio 2022 ou qualquer ambiente com suporte a WPF e .NET

## Dependências do projeto

- WPF (.NET 8)
- Microsoft.Web.WebView2
- NuGet será responsável por restaurar as dependências automaticamente ao compilar

## Compilar

No terminal, dentro da pasta do projeto:

```powershell
dotnet restore
dotnet build "TransparentChatFilter.csproj" -c Release
```

Se preferir compilar sem restore explícito:

```powershell
dotnet build "TransparentChatFilter.csproj" -c Release
```

## Executar

Após a compilação, você pode abrir o executável gerado em:

```powershell
bin\Release\net8.0-windows\TransparentChatFilter.exe
```

Ou executar diretamente via dotnet:

```powershell
dotnet run --project "TransparentChatFilter.csproj"
```

## Observações

- O projeto usa WPF e, por isso, só pode ser executado em Windows.
- O diretório `bin/` e `obj/` são artefatos de compilação e devem ser ignorados pelo Git conforme configurado em `.gitignore`.
- Caso queira gerar uma versão publicada, você pode usar:

```powershell
dotnet publish "TransparentChatFilter.csproj" -c Release -r win-x64 --self-contained false
```
