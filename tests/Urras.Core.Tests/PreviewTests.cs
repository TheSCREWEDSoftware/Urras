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

    // READMEs mix HTML into Markdown: pictures, <details> and tables are drawn, not shown as tags
    public static void ReadsHtmlInsideMarkdown()
    {
        const string README = "# Urras\n\n<img src=\"logo.png\" alt=\"Logo\" width=\"96\">\n\n" +
                              "<details>\n<summary><b>Diffs</b></summary>\n\n- One\n\n</details>\n\n" +
                              "<table>\n  <tr>\n    <td>A cell</td>\n  </tr>\n</table>\n\n" +
                              "Text with <b>bold</b> and <a href=\"x.md\">a link</a>.<br>Next\n";
        List<MarkdownBlock> blocks = MarkdownDocument.Parse(README);
        string all = string.Join(" ", blocks.SelectMany(block => block.Spans.Select(span => span.Text)));
        Assert.True(!all.Contains('<'), "no tags left as text: " + all);
        Assert.True(blocks.Any(block => block.Spans.Any(span => span.Style.HasFlag(SpanStyle.Image) &&
                                                                span.Url == "logo.png")), "the picture");
        Assert.True(blocks.Any(block => block.Spans.Any(span => span.Text == "Diffs" &&
                                                                span.Style.HasFlag(SpanStyle.Bold))), "summary");
        Assert.True(blocks.Any(block => block.Kind == BlockKind.ListItem), "Markdown inside details");
        Assert.True(blocks.Any(block => block.Kind == BlockKind.Table), "the table");
        MarkdownBlock last = blocks[^1];
        Assert.True(last.Spans.Any(span => span.Text == "bold" && span.Style.HasFlag(SpanStyle.Bold)), "inline bold");
        Assert.True(last.Spans.Any(span => span.Url == "x.md"), "inline link");
    }

    public static void FindsPicturesInTheRepository()
    {
        Assert.Equal("src/Urras.App/Assets/urras.png",
                     PreviewFiles.PicturePath("README.md", "src/Urras.App/Assets/urras.png"), "beside the file");
        Assert.Equal("docs/img/a b.png", PreviewFiles.PicturePath("docs/guide.md", "img/a%20b.png?raw=1"),
                     "relative to the file's folder, unescaped, without the query");
        Assert.Equal("logo.png", PreviewFiles.PicturePath("docs/guide.md", "../logo.png"), "a folder up");
        Assert.Equal("logo.png", PreviewFiles.PicturePath("docs/guide.md", "/logo.png"), "from the top");
        Assert.True(PreviewFiles.PicturePath("README.md", "../outside.png") == null, "never outside the repository");
        Assert.True(PreviewFiles.PicturePath("README.md", "https://img.shields.io/x.svg") == null, "internet");
        Assert.Equal(3, PreviewFiles.DataPicture("data:image/png;base64,AQID")!.Length, "data: pictures");
        Assert.True(PreviewFiles.DataPicture("data:text/plain;base64,AQID") == null, "only pictures");
    }

    public static void ReadsNotebooks()
    {
        const string NOTEBOOK = "{\"metadata\":{\"language_info\":{\"name\":\"python\"}},\"cells\":[" +
                                "{\"cell_type\":\"markdown\",\"source\":[\"# Results\\n\",\"Some *notes*\"]}," +
                                "{\"cell_type\":\"code\",\"source\":\"print(1)\",\"outputs\":[" +
                                "{\"output_type\":\"stream\",\"text\":[\"1\\n\"]}," +
                                "{\"output_type\":\"display_data\",\"data\":{\"image/png\":\"AQID\"}}]}]}";
        List<MarkdownBlock> blocks = NotebookDocument.Parse(NOTEBOOK);
        Assert.Equal(BlockKind.Heading, blocks[0].Kind, "Markdown cells are drawn");
        MarkdownBlock code = blocks.First(block => block.Kind == BlockKind.Code);
        Assert.Equal("print(1)", code.Text, "code cells");
        Assert.Equal("python", code.Marker, "with the notebook's language");
        Assert.True(blocks.Any(block => block.Marker == "output" && block.Text == "1"), "printed output");
        Assert.True(blocks.Any(block => block.Spans.Any(span => span.Url == "data:image/png;base64,AQID")),
                    "pictures the cell drew");
        Assert.Equal(0, NotebookDocument.Parse("not json").Count, "a broken notebook shows nothing, not an error");
    }

    public static void ReadsRestructuredText()
    {
        const string RST = "=====\nUrras\n=====\n\nA *fast* client, see `the docs <https://x.y>`_.\n\n" +
                           "Install\n-------\n\n* One\n* Two ``code``\n\nRun this::\n\n    dotnet run\n\n" +
                           ".. note:: Windows and Linux.\n\n.. image:: logo.png\n";
        List<MarkdownBlock> blocks = RestructuredTextDocument.Parse(RST);
        Assert.Equal(1, blocks[0].Level, "an over- and underlined title");
        Assert.True(blocks.Any(block => block.Kind == BlockKind.Heading && block.Level == 2), "a section heading");
        Assert.True(blocks.Any(block => block.Spans.Any(span => span.Url == "https://x.y")), "links");
        Assert.Equal(2, blocks.Count(block => block.Kind == BlockKind.ListItem), "bullet list");
        Assert.Equal("dotnet run", blocks.First(block => block.Kind == BlockKind.Code).Text, "literal block");
        Assert.True(blocks.Any(block => block.Kind == BlockKind.Quote), "notes");
        Assert.True(blocks.Any(block => block.Spans.Any(span => span.Url == "logo.png")), "images");
    }

    public static void ReadsAsciiDoc()
    {
        const string ADOC = "= Urras\n:toc:\n\nA *fast* client, see https://x.y[the docs].\n\n== Install\n\n" +
                            "* One\n** Nested\n\n[source,bash]\n----\ndotnet run\n----\n\nNOTE: Windows too.\n\n" +
                            "image::logo.png[Logo]\n\n|===\n| Key | Value\n| a | b\n|===\n";
        List<MarkdownBlock> blocks = AsciiDocDocument.Parse(ADOC);
        Assert.Equal(BlockKind.Heading, blocks[0].Kind, "the title; attributes like :toc: are left out");
        Assert.True(blocks[1].Spans.Any(span => span.Text == "fast" && span.Style.HasFlag(SpanStyle.Bold)), "bold");
        Assert.True(blocks[1].Spans.Any(span => span.Url == "https://x.y"), "links");
        Assert.Equal(1, blocks.First(block => block.Kind == BlockKind.ListItem && block.Level == 1).Level, "nesting");
        MarkdownBlock code = blocks.First(block => block.Kind == BlockKind.Code);
        Assert.Equal("bash", code.Marker, "the source language");
        Assert.True(blocks.Any(block => block.Kind == BlockKind.Quote), "admonitions");
        Assert.True(blocks.Any(block => block.Spans.Any(span => span.Url == "logo.png")), "images");
        Assert.Equal(2, blocks.First(block => block.Kind == BlockKind.Table).Rows.Count, "tables");
    }
}
