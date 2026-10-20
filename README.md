# Transparent Chat Filter

Este programa foi feito para quem quer acompanhar o chat da Twitch sem atrapalhar a tela principal.

Ele abre como uma janela flutuante, transparente e fácil de ler, ideal para assistir lives, jogar ou acompanhar conversas ao mesmo tempo.

## Download da versão pronta

A forma mais simples de usar é baixar o arquivo já compilado:

- [Baixar para Windows](https://github.com/Sanzenkai01/TransparentChatFilterUnited/releases/download/v1.0.1/TransparentChatFilter-win-x64.zip)

Como usar:

1. baixe o arquivo ZIP
2. extraia o arquivo `TransparentChatFilter.exe` para uma pasta no computador
3. abra o `TransparentChatFilter.exe`
4. pronto: o programa já pode ser usado

## O que ele faz

- mostra o chat em uma janela sobreposta
- deixa a tela mais limpa e discreta
- esconde mensagens que você não quer ver
- permite bloquear usuários, canais e termos
- ajuda a destacar mensagens importantes
- funciona bem enquanto você assiste ou joga

## Requisitos

- Windows 10 ou 11
- nenhum conhecimento de programação é necessário para usar a versão pronta

## Se quiser compilar no computador

Se você abrir o projeto e quiser gerar a versão manualmente, use:

```powershell
dotnet restore
dotnet build "TransparentChatFilter.csproj" -c Release
```

O executável será gerado em:

```powershell
bin\Release\net8.0-windows\TransparentChatFilter.exe
```

## Observações

- o ZIP contém somente o executável; os componentes necessários foram incluídos nele
- ao iniciar, o Windows e o WebView2 podem criar dados temporários e configurações locais para o funcionamento normal do programa
- o programa foi pensado para Windows
- para usuários leigos, o melhor caminho é usar o arquivo ZIP da release
