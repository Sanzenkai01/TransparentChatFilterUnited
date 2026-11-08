using System.Text.Json;
using TransparentChatFilter;
using Xunit;

namespace TransparentChatFilter.Tests;

public sealed class ReleaseChangelogTests
{
    [Fact]
    public void FormatsCommitSubjectsAndOmitsFullChangelogLink()
    {
        const string releaseBody = """
            ## Novidades
            * Melhorias no atualizador
            **Full Changelog**: https://github.com/Sanzenkai01/TransparentChatFilterUnited/compare/v1.2.3...v1.2.4
            """;

        var changelog = ReleaseChangelog.Format(
            "1.2.3",
            "v1.2.4",
            releaseBody,
            ["Corrige a janela de novidades\n\nDetalhes adicionais\n\nCo-authored-by: autor"],
            totalCommitCount: 1);

        Assert.Contains("Novidades", changelog);
        Assert.Contains("- Melhorias no atualizador", changelog);
        Assert.Contains("- Corrige a janela de novidades", changelog);
        Assert.Contains("  Detalhes adicionais", changelog);
        Assert.DoesNotContain("Full Changelog", changelog);
        Assert.DoesNotContain("github.com", changelog);
        Assert.DoesNotContain("Co-authored-by", changelog);
    }

    [Fact]
    public void ReadsCommitMessagesAndTotalCountFromCompareResponse()
    {
        using var document = JsonDocument.Parse(
            """{"total_commits":1,"commits":[{"commit":{"message":"Título do commit\n\nCorpo"}}]}""");

        Assert.Equal(1, ReleaseChangelog.ReadTotalCommitCount(document.RootElement));
        Assert.Equal(
            ["Título do commit\n\nCorpo"],
            ReleaseChangelog.ReadCommitMessages(document.RootElement));
    }

    [Fact]
    public void ReportsWhenGitHubReturnsOnlyPartOfTheCommitList()
    {
        var changelog = ReleaseChangelog.Format(
            "1.2.3",
            "v1.2.4",
            string.Empty,
            ["Alteração incluída"],
            totalCommitCount: 2);

        Assert.Contains("1 de 2 alterações", changelog);
    }
}
