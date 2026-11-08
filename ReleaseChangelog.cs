using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace TransparentChatFilter;

internal static partial class ReleaseChangelog
{
    private const int MaximumCommitCount = 250;
    private const int MaximumChangelogCharacters = 256 * 1024;

    internal static string Format(
        string fromVersion,
        string toVersion,
        string releaseBody,
        IReadOnlyList<string> commitMessages,
        int totalCommitCount)
    {
        var sections = new List<string>();
        var cleanedReleaseBody = CleanReleaseBody(releaseBody);
        if (!string.IsNullOrWhiteSpace(cleanedReleaseBody))
        {
            sections.Add(cleanedReleaseBody);
        }

        if (commitMessages.Count > 0)
        {
            var commits = commitMessages
                .Select(FormatCommitMessage)
                .Where(subject => !string.IsNullOrWhiteSpace(subject))
                .Select(subject => $"- {subject}");
            var commitSection = new StringBuilder()
                .AppendLine($"Alterações desde a versão {fromVersion} até {toVersion}:")
                .Append(string.Join(Environment.NewLine, commits))
                .ToString();
            if (totalCommitCount > commitMessages.Count)
            {
                commitSection += $"{Environment.NewLine}{Environment.NewLine}" +
                                 $"O changelog exibido inclui {commitMessages.Count} de {totalCommitCount} alterações.";
            }

            sections.Add(commitSection);
        }
        else if (sections.Count == 0)
        {
            sections.Add("Não há detalhes de alterações publicados para esta versão.");
        }

        var changelog = string.Join($"{Environment.NewLine}{Environment.NewLine}", sections);
        if (changelog.Length <= MaximumChangelogCharacters)
        {
            return changelog;
        }

        const string truncatedNotice = "Changelog encurtado para caber no armazenamento temporário.";
        var end = changelog.LastIndexOf(
            Environment.NewLine,
            MaximumChangelogCharacters - truncatedNotice.Length,
            StringComparison.Ordinal);
        if (end < 0)
        {
            end = MaximumChangelogCharacters - truncatedNotice.Length;
        }

        return $"{changelog[..end]}{Environment.NewLine}{truncatedNotice}";
    }

    internal static List<string> ReadCommitMessages(JsonElement comparison)
    {
        if (!comparison.TryGetProperty("commits", out var commits) ||
            commits.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException("A resposta do GitHub não contém a lista de commits.");
        }

        var messages = new List<string>(commits.GetArrayLength());
        foreach (var commit in commits.EnumerateArray())
        {
            if (commit.ValueKind != JsonValueKind.Object ||
                !commit.TryGetProperty("commit", out var commitDetails) ||
                commitDetails.ValueKind != JsonValueKind.Object ||
                !commitDetails.TryGetProperty("message", out var message) ||
                message.ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(message.GetString()))
            {
                throw new InvalidDataException("A resposta do GitHub contém um commit inválido.");
            }

            messages.Add(message.GetString()!);
        }

        return messages;
    }

    internal static int ReadTotalCommitCount(JsonElement comparison)
    {
        if (!comparison.TryGetProperty("total_commits", out var totalCommits) ||
            !totalCommits.TryGetInt32(out var count) ||
            count < 0)
        {
            throw new InvalidDataException("A resposta do GitHub não contém uma contagem de commits válida.");
        }

        return count;
    }

    internal static int MaximumCommits => MaximumCommitCount;

    private static string CleanReleaseBody(string releaseBody)
    {
        var lines = releaseBody
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Where(line => !line.TrimStart().StartsWith("**Full Changelog**", StringComparison.OrdinalIgnoreCase) &&
                           !line.TrimStart().StartsWith("Full Changelog:", StringComparison.OrdinalIgnoreCase))
            .Select(line =>
            {
                var cleanedLine = MarkdownLink().Replace(line, "$1")
                    .Replace("**", string.Empty, StringComparison.Ordinal)
                    .Replace("`", string.Empty, StringComparison.Ordinal)
                    .TrimStart('#', ' ')
                    .Trim();
                return cleanedLine.StartsWith("* ", StringComparison.Ordinal)
                    ? $"- {cleanedLine[2..]}"
                    : cleanedLine;
            })
            .Where(line => !string.IsNullOrWhiteSpace(line));
        return string.Join(Environment.NewLine, lines);
    }

    private static string FormatCommitMessage(string message)
    {
        var lines = message.Trim().Split(['\r', '\n'], StringSplitOptions.None);
        var trailerStart = Array.FindIndex(lines, line =>
            line.StartsWith("Co-authored-by:", StringComparison.OrdinalIgnoreCase) ||
            line.StartsWith("Signed-off-by:", StringComparison.OrdinalIgnoreCase) ||
            line.StartsWith("Reviewed-by:", StringComparison.OrdinalIgnoreCase));
        if (trailerStart >= 0)
        {
            lines = lines[..trailerStart];
        }

        var content = lines
            .Select(line => line.Trim())
            .ToArray();
        if (content.Length == 0)
        {
            return string.Empty;
        }

        var body = string.Join(
            Environment.NewLine,
            content.Skip(1).Select(line => $"  {line}").Where(line => !string.IsNullOrWhiteSpace(line)));
        return string.IsNullOrEmpty(body)
            ? content[0]
            : $"{content[0]}{Environment.NewLine}{body}";
    }

    [GeneratedRegex(@"\[([^\]]+)\]\([^)]+\)", RegexOptions.CultureInvariant)]
    private static partial Regex MarkdownLink();
}
