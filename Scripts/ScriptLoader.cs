using System.IO;

namespace TransparentChatFilter;

internal static class ScriptLoader
{
    internal static string Load(string fileName)
    {
        var resourceName = $"TransparentChatFilter.Scripts.{fileName}";
        using var stream = typeof(ScriptLoader).Assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"O recurso incorporado '{resourceName}' não foi encontrado.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd().TrimEnd('\r', '\n');
    }
}
