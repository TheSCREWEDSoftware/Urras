using Urras.Core.Markdown;

namespace Urras.Core.Tests;

public static class PreviewTests
{
    public static void KnowsWhichFilesHaveAPreview()
    {
        Assert.Equal(PreviewKind.Markdown, PreviewFiles.KindOf("docs/README.md"), "Markdown");
        Assert.Equal(PreviewKind.Html, PreviewFiles.KindOf("site/INDEX.HTML"), "HTML, any case");
        Assert.Equal(PreviewKind.Table, PreviewFiles.KindOf("data/items.tsv"), "TSV");
        Assert.True(!PreviewFiles.CanPreview("src/App.cs"), "code has no preview");
    }

    public static void ReadsHtmlIntoBlocks()
    {
        const string HTML = "<!doctype html><html><head><title>x</title><style>p{color:red}</style></head><body>" +
                            "<h2>Urras &amp; Anarres</h2><p>A <b>fast</b>,\n   native <a href=\"https://x.y\">client</a>." +
                            "<br>Second line</p><script>alert(1)</script><ul><li>One</li><li>Two<ol><li>Inner</li>" +
                            "</ol></li></ul><pre>  code\n  kept</pre><blockquote>Quoted</blockquote><hr>" +
                            "<table><tr><th>Name</th><th>Size</th></tr><tr><td>a</td><td>1</td></tr></table></body>";
        List<MarkdownBlock> blocks = HtmlDocument.Parse(HTML);
        Assert.Equal(BlockKind.Heading, blocks[0].Kind, "the heading first; head, styles and scripts left out");
        Assert.Equal("Urras & Anarres", string.Concat(blocks[0].Spans.Select(span => span.Text)), "entities");
        MarkdownBlock paragraph = blocks[1];
        Assert.True(paragraph.Spans.Any(span => span.Text == "fast" && span.Style.HasFlag(SpanStyle.Bold)), "bold");
        Assert.True(paragraph.Spans.Any(span => span.Url == "https://x.y" && span.Style.HasFlag(SpanStyle.Link)),
                    "links keep their address");
        Assert.True(string.Concat(paragraph.Spans.Select(span => span.Text)).Contains("fast, native"),
                    "spaces collapse as in a browser");
        Assert.True(!blocks.Any(block => block.Source.Contains("alert")), "scripts are not shown");
        List<MarkdownBlock> items = blocks.Where(block => block.Kind == BlockKind.ListItem).ToList();
        Assert.Equal(3, items.Count, "list items, nested too");
        Assert.Equal("1.", items[2].Marker, "numbered lists are numbered");
        Assert.Equal(1, items[2].Level, "nesting");
        Assert.Equal("  code\n  kept", blocks.First(block => block.Kind == BlockKind.Code).Text, "pre kept as is");
        Assert.True(blocks.Any(block => block.Kind == BlockKind.Quote), "quotes");
        Assert.True(blocks.Any(block => block.Kind == BlockKind.Rule), "rules");
        MarkdownBlock table = blocks.First(block => block.Kind == BlockKind.Table);
        Assert.Equal(2, table.Rows.Count, "table rows");
        Assert.Equal("Size", table.Rows[0][1][0].Text, "table cells");
    }

    public static void ReadsCsvWithQuotes()
    {
        List<List<string>> rows = DelimitedTable.Rows("name,notes\r\n\"Smith, J\",\"said \"\"hi\"\"\nthen left\"\n,\n",
                                                     ',');
        Assert.Equal(3, rows.Count, "three rows, one with a line break inside quotes");
        Assert.Equal("Smith, J", rows[1][0], "separator inside quotes");
        Assert.Equal("said \"hi\"\nthen left", rows[1][1], "doubled quotes and a line break");
        Assert.Equal(2, rows[2].Count, "empty fields count");

        List<MarkdownBlock> before = DelimitedTable.Parse("a\tb\n1\t2\n3\t4\n", '\t');
        List<MarkdownBlock> after = DelimitedTable.Parse("a\tb\n1\t2\n3\t5\n", '\t');
        (HashSet<int> removed, HashSet<int> added) = MarkdownDocument.Changes(before, after);
        Assert.True(removed.SetEquals(new[] { 2 }) && added.SetEquals(new[] { 2 }), "only the changed row is marked");
    }
}
