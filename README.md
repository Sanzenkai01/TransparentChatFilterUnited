# TransparentChatFilterUnited

Este programa foi feito para quem quer acompanhar o chat da Twitch sem atrapalhar a tela principal.

Ele abre como uma janela flutuante, transparente e fácil de ler, ideal para assistir lives, jogar ou acompanhar conversas ao mesmo tempo.

## Download da versão pronta

A forma mais simples de usar é baixar o arquivo já compilado:

- [Baixar para Windows](https://github.com/Sanzenkai01/TransparentChatFilterUnited/releases/latest/download/TransparentChatFilterUnited-win-x64.zip)

Como usar:

1. baixe o arquivo ZIP
2. extraia o arquivo `TransparentChatFilterUnited.exe` para uma pasta no computador
3. abra o `TransparentChatFilterUnited.exe`
4. pronto: o programa já pode ser usado

## Atualizações

O programa verifica as atualizações ao iniciar. Quando uma nova versão estiver disponível, ele pedirá confirmação, baixará o pacote da release oficial no GitHub e reiniciará já atualizado.

Para publicar uma versão, atualize a versão em `TransparentChatFilter.csproj`, crie e envie uma tag correspondente (por exemplo, `v1.1.5`). O GitHub Actions compilará o executável e publicará o ZIP usado pelo atualizador:

```powershell
git tag v1.1.5
git push origin v1.1.5
```

Quem já usa uma versão anterior a `1.0.3` precisa baixar esta versão manualmente uma vez; as versões antigas não incluem o atualizador.

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
bin\Release\net8.0-windows\TransparentChatFilterUnited.exe
```

## Observações

- o ZIP contém somente o executável; os componentes necessários foram incluídos nele
- ao iniciar, o Windows e o WebView2 podem criar dados temporários e configurações locais para o funcionamento normal do programa
- o programa foi pensado para Windows
- para usuários leigos, o melhor caminho é usar o arquivo ZIP da release
