using System.Collections.Concurrent;
using System.Reflection;
using System.Text;

namespace BlazorVirtualize.Demo.Components;

/// <summary>
/// Loads the embedded source of a demo page and strips the demo-shell specific
/// parts (routing/render-mode directives, page title, the page header block and
/// the source-viewer component itself) so only the example itself is shown.
/// </summary>
public static class DemoSourceLoader
{
    private static readonly ConcurrentDictionary<string, string> Cache = new();
    private static readonly Assembly Asm = typeof(DemoSourceLoader).Assembly;

    /// <param name="page">The page file name without extension, e.g. "FixedDemo".</param>
    public static string Load(string page) => Cache.GetOrAdd(page, LoadCore);

    private static string LoadCore(string page)
    {
        var suffix = $".{page}.razor";
        var resourceName = Array.Find(Asm.GetManifestResourceNames(),
            n => n.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));

        if (resourceName is null)
            return $"// Source for '{page}' was not found.";

        using var stream = Asm.GetManifestResourceStream(resourceName);
        if (stream is null)
            return $"// Source for '{page}' could not be read.";

        using var reader = new StreamReader(stream);
        return Strip(reader.ReadToEnd());
    }

    private static string Strip(string source)
    {
        var lines = source.Replace("\r\n", "\n").Split('\n');
        var result = new List<string>(lines.Length);
        var inPageHead = false;

        foreach (var line in lines)
        {
            var trimmed = line.TrimStart();

            // Drop the page-head block (page title + description), including nested lines.
            if (inPageHead)
            {
                if (trimmed.StartsWith("</div>", StringComparison.Ordinal))
                    inPageHead = false;
                continue;
            }
            if (trimmed.StartsWith("<div class=\"page-head\"", StringComparison.Ordinal))
            {
                inPageHead = true;
                continue;
            }

            // Drop routing / render-mode directives.
            if (trimmed.StartsWith("@page", StringComparison.Ordinal) ||
                trimmed.StartsWith("@rendermode", StringComparison.Ordinal))
                continue;

            // Drop the <PageTitle>...</PageTitle> element.
            if (trimmed.StartsWith("<PageTitle", StringComparison.Ordinal))
                continue;

            // Drop the source-viewer component instance itself.
            if (trimmed.StartsWith("<DemoSource", StringComparison.Ordinal))
                continue;

            result.Add(line);
        }

        // Collapse leading/trailing and repeated blank lines.
        var sb = new StringBuilder();
        var blankRun = 0;
        var started = false;
        foreach (var line in result)
        {
            if (line.Trim().Length == 0)
            {
                blankRun++;
                if (!started || blankRun > 1)
                    continue;
                sb.Append('\n');
                continue;
            }

            blankRun = 0;
            started = true;
            sb.Append(line).Append('\n');
        }

        return sb.ToString().TrimEnd('\n');
    }
}
