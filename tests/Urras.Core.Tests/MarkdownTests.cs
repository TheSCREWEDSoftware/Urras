using Urras.Core.Markdown;

namespace Urras.Core.Tests;

public static class MarkdownTests
{
    private const string README = "# Urras\n\nA **Git** client, *fast* and `small`.\nSecond line.\n\n" +
                                  "## Build\n\n- Install .NET\n  then restart\n- Run [the script](build.sh)\n" +
                                  "  1. nested\n\n> Note: Windows\n> and Linux\n\n```bash\ndotnet build\n```\n\n" +
                                  "| Key | Value |\n|---|:---:|\n| a | **b** |\n\n---\n- [x] done\n";

    public static void ReadsBlocks()
    {
        List<MarkdownBlock> blocks = MarkdownDocument.Parse(README);
        Assert.Equal("Heading|Paragraph|Heading|ListItem|ListItem|ListItem|Quote|Code|Table|Rule|ListItem",
                     string.Join("|", blocks.Select(block => block.Kind)), "block kinds in order");
        Assert.Equal(1, blocks[0].Level, "heading level");
        Assert.Equal("Install .NET then restart", string.Concat(blocks[3].Spans.Select(span => span.Text)),
                     "an item's indented line joins it");
        Assert.Equal(1, blocks[5].Level, "nested item");
        Assert.Equal("1.", blocks[5].Marker, "numbered");
        Assert.Equal("Note: Windows and Linux", blocks[6].Spans[0].Text, "quote lines joined");
        Assert.Equal("bash", blocks[7].Marker, "code language");
        Assert.Equal("dotnet build", blocks[7].Text, "code kept as it is");
        Assert.Equal(2, blocks[8].Rows.Count, "header and one row");
        Assert.Equal(SpanStyle.Bold, blocks[8].Rows[1][1][0].Style, "styled cell");
        Assert.Equal("[done] done", blocks[10].Spans[0].Text, "task box as a plain marker");
    }

    public static void ReadsInlineStyles()
    {
        List<MarkdownSpan> spans = MarkdownDocument.Inline(
            "Plain **bold *both*** `code` ~~gone~~ [link](https://a.b \"t\") ![logo](l.png) snake_case_name \\*x");
        Assert.True(spans.Any(span => span.Text == "bold " && span.Style == SpanStyle.Bold), "bold");
        Assert.True(spans.Any(span => span.Text == "both" && span.Style == (SpanStyle.Bold | SpanStyle.Italic)),
                    "nested styles");
        Assert.True(spans.Any(span => span.Text == "code" && span.Style == SpanStyle.Code), "code");
        Assert.True(spans.Any(span => span.Text == "gone" && span.Style == SpanStyle.Strike), "strike");
        Assert.True(spans.Any(span => span.Text == "link" && span.Url == "https://a.b"), "link without its title");
        Assert.True(spans.Any(span => span.Text == "logo" && span.Style == SpanStyle.Image), "image");
        Assert.True(spans.Any(span => span.Text.Contains("snake_case_name *x")), "underscores in words, escapes");
    }

    public static void FindsChangedBlocks()
    {
        List<MarkdownBlock> before = MarkdownDocument.Parse("# Title\n\nSame.\n\nOld paragraph.\n\n- a\n");
        List<MarkdownBlock> after = MarkdownDocument.Parse("# Title\n\nSame.\n\nNew paragraph.\n\n- a\n- b\n");
        (HashSet<int> removed, HashSet<int> added) = MarkdownDocument.Changes(before, after);
        Assert.Equal("2", string.Join(",", removed), "the old paragraph");
        Assert.Equal("2,4", string.Join(",", added.OrderBy(index => index)), "the new paragraph and item");
        Assert.Equal(0, MarkdownDocument.Changes(before, before).Added.Count, "no changes");
    }
}
