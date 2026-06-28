using System.Collections.Concurrent;
using System.Text;

namespace BlazorVirtualize.Demo.Components;

/// <summary>
/// Loads the source of a demo page from disk and strips the demo-shell specific parts
/// (routing/render-mode directives, page title, the page header block and the source-viewer
/// component itself) so only the example itself is shown.
/// </summary>
public static class DemoSourceLoader
{
    private static readonly ConcurrentDictionary<string, string> Cache = new();

    /// <param name="contentRoot">The application's content root path.</param>
    /// <param name="page">The page file name without extension, e.g. "FixedDemo".</param>
    /// <param name="section">
    /// Optional region name. When supplied, only the lines between
    /// <c>@* region:{section} *@</c> and <c>@* endregion *@</c> are returned.
    /// </param>
    public static string Load(string contentRoot, string page, string? section = null) =>
        Cache.GetOrAdd($"{page}::{section}", _ => LoadCore(contentRoot, page, section));

    private static string LoadCore(string contentRoot, string page, string? section)
    {
        // Probe the likely locations for the page source.
        var candidates = new[]
        {
            Path.Combine(contentRoot, "Components", "Pages", page + ".razor"),
            Path.Combine(AppContext.BaseDirectory, "Components", "Pages", page + ".razor"),
        };

        var path = Array.Find(candidates, File.Exists);
        if (path is null)
        {
            return $"// Source for '{page}.razor' was not found.";
        }

        var text = File.ReadAllText(path);
        if (!string.IsNullOrEmpty(section))
        {
            text = ExtractRegion(text, section);
        }

        return Strip(text);
    }

    private static string ExtractRegion(string source, string section)
    {
        var lines = source.Replace("\r\n", "\n").Split('\n');
        var captured = new List<string>();
        var inRegion = false;

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (!inRegion)
            {
                if (trimmed.StartsWith("@*", StringComparison.Ordinal) &&
                    trimmed.Contains("region:" + section, StringComparison.Ordinal))
                {
                    inRegion = true;
                }
                continue;
            }

            if (trimmed.StartsWith("@*", StringComparison.Ordinal) &&
                trimmed.Contains("endregion", StringComparison.Ordinal))
            {
                break;
            }
            captured.Add(line);
        }

        return captured.Count == 0
            ? $"// Region '{section}' was not found."
            : string.Join('\n', captured);
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

            // Drop region markers (e.g. @* region:rail1 *@ / @* endregion *@).
            if (trimmed.StartsWith("@*", StringComparison.Ordinal) &&
                (trimmed.Contains("region:", StringComparison.Ordinal) ||
                 trimmed.Contains("endregion", StringComparison.Ordinal)))
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
