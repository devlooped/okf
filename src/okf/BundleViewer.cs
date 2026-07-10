using System.Text;
using System.Text.Json;

namespace Devlooped;

public sealed record ViewerStats(int Concepts, int Edges, int Bytes);

/// <summary>
/// Embeds an Obsidian Publish–style reader HTML shell for a knowledge graph.
/// </summary>
public static class BundleViewer
{
    static readonly JsonSerializerOptions JsonOptions = new(GraphBuilder.JsonOptions)
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static ViewerStats Generate(GraphBuilder.KnowledgeGraph graph, string outPath, string? displayName = null)
    {
        outPath = Path.GetFullPath(outPath);
        var name = displayName ?? "";

        var html = ThisAssembly.Resources.View.view_template.Text
            .Replace("/*__VIEW_CSS__*/", ThisAssembly.Resources.View.view_styles.Text, StringComparison.Ordinal)
            .Replace("/*__VIEW_JS__*/", ThisAssembly.Resources.View.view_script.Text, StringComparison.Ordinal)
            .Replace("__BUNDLE_NAME__", JsonSerializer.Serialize(name, JsonOptions), StringComparison.Ordinal)
            .Replace("__GRAPH_DATA__", JsonSerializer.Serialize(graph, JsonOptions), StringComparison.Ordinal);

        var dir = Path.GetDirectoryName(outPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        File.WriteAllText(outPath, html, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        return new ViewerStats(
            graph.Nodes.Count,
            graph.Edges.Count,
            Encoding.UTF8.GetByteCount(html));
    }
}
