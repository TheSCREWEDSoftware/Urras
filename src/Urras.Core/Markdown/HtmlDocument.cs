using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace Urras.Core.Markdown;

// HTML read into Markdown's blocks and inline styles, for reading: styles, scripts and layout are left out
public static partial class HtmlDocument
{
    private const int MAX_HEADING_LEVEL = 6;

    [GeneratedRegex(@"<!--.*?-->|<!\[CDATA\[.*?\]\]>|<![^>]*>|<\?[^>]*\?>", RegexOptions.Singleline)]
    private static partial Regex CommentRegex();

    [GeneratedRegex(@"<(script|style|head|template|noscript|svg)\b.*?</\1\s*>",
                    RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex HiddenRegex();

    [GeneratedRegex(@"<(/?)([a-zA-Z][a-zA-Z0-9]*)((?:[^>""']|""[^""]*""|'[^']*')*?)(/?)>")]
    private static partial Regex TagRegex();

    [GeneratedRegex(@"\b(href|src|alt|width)\s*=\s*(""([^""]*)""|'([^']*)'|([^\s>]+))", RegexOptions.IgnoreCase)]
    private static partial Regex AttributeRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex SpaceRegex();

    // Tags that end the paragraph being read, whatever they are
    private static readonly HashSet<string> BlockTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "p", "div", "section", "article", "header", "footer", "main", "nav", "aside", "figure", "figcaption",
        "body", "html", "form", "fieldset", "details", "summary", "dl", "dt", "dd", "address", "center"
    };

    public static List<MarkdownBlock> Parse(string html)
    {
        Reader reader = new Reader();
        string text = HiddenRegex().Replace(CommentRegex().Replace(html, string.Empty), string.Empty);
        int position = 0;
        foreach (Match tag in TagRegex().Matches(text))
        {
            reader.Text(text[position..tag.Index]);
            position = tag.Index + tag.Length;
            string name = tag.Groups[2].Value.ToLowerInvariant();
            bool closing = tag.Groups[1].Value == "/";
            if (closing)
                reader.Close(name);
            else
                reader.Open(name, tag.Groups[3].Value);
        }

        reader.Text(text[position..]);
        reader.Flush();
        return reader.Blocks;
    }

    private static string? Attribute(string attributes, string name)
    {
        foreach (Match match in AttributeRegex().Matches(attributes))
        {
            if (!match.Groups[1].Value.Equals(name, StringComparison.OrdinalIgnoreCase))
                continue;
            string value = match.Groups[3].Success ? match.Groups[3].Value
                         : match.Groups[4].Success ? match.Groups[4].Value
                                                   : match.Groups[5].Value;
            return WebUtility.HtmlDecode(value);
        }

        return null;
    }

    // Reads tags and text in order, keeping the block being built and the inline styles that are open
    private sealed class Reader
    {
        private readonly List<MarkdownSpan> _spans = new List<MarkdownSpan>();
        private readonly StringBuilder _source = new StringBuilder();
        private readonly Stack<SpanStyle> _styles = new Stack<SpanStyle>();
        private readonly Stack<string?> _links = new Stack<string?>();
        private readonly Stack<int?> _lists = new Stack<int?>();
        private readonly StringBuilder _code = new StringBuilder();
        private BlockKind _kind = BlockKind.Paragraph;
        private int _level;
        private string? _marker;
        private int _quoteDepth;
        private bool _inCode;
        private List<List<List<MarkdownSpan>>>? _table;
        private List<List<MarkdownSpan>>? _row;

        public List<MarkdownBlock> Blocks { get; } = new List<MarkdownBlock>();

        private SpanStyle Style => _styles.Aggregate(SpanStyle.None, (all, style) => all | style);
        private string? Link => _links.FirstOrDefault(link => link != null);

        public void Text(string raw)
        {
            if (raw.Length == 0)
                return;
            string text = WebUtility.HtmlDecode(raw);
            if (_inCode)
            {
                _code.Append(text);
                return;
            }

            text = SpaceRegex().Replace(text, " ");
            // Spaces at the start of a block, or doubled across tags, are dropped as a browser does
            if (_spans.Count == 0 || _spans[^1].Text.EndsWith(' ') || _spans[^1].Text.EndsWith('\n'))
                text = text.TrimStart();
            if (text.Length == 0)
                return;
            SpanStyle style = Style;
            string? link = Link;
            if (link != null)
                style |= SpanStyle.Link;
            _spans.Add(new MarkdownSpan(text, style, link));
            _source.Append(text);
        }

        public void Open(string name, string attributes)
        {
            switch (name)
            {
                case "h1" or "h2" or "h3" or "h4" or "h5" or "h6":
                {
                    Flush();
                    _kind = BlockKind.Heading;
                    _level = Math.Min(name[1] - '0', MAX_HEADING_LEVEL);
                    break;
                }
                case "br":
                {
                    _spans.Add(new MarkdownSpan("\n", SpanStyle.None));
                    _source.Append('\n');
                    break;
                }
                case "hr":
                {
                    Flush();
                    Blocks.Add(new MarkdownBlock { Kind = BlockKind.Rule, Source = "---" });
                    break;
                }
                case "pre":
                {
                    Flush();
                    _inCode = true;
                    _code.Clear();
                    break;
                }
                case "blockquote":
                {
                    Flush();
                    ++_quoteDepth;
                    break;
                }
                case "ul" or "ol":
                {
                    Flush();
                    _lists.Push(name == "ol" ? 0 : null);
                    break;
                }
                case "li":
                {
                    Flush();
                    _kind = BlockKind.ListItem;
                    _level = Math.Max(0, _lists.Count - 1);
                    if (_lists.Count > 0 && _lists.Peek() is { } number)
                    {
                        _lists.Pop();
                        _lists.Push(number + 1);
                        _marker = number + 1 + ".";
                    }
                    else
                        _marker = "-";
                    break;
                }
                case "table":
                {
                    Flush();
                    _table = new List<List<List<MarkdownSpan>>>();
                    break;
                }
                case "tr":
                {
                    EndCell();
                    _row = new List<List<MarkdownSpan>>();
                    break;
                }
                case "td" or "th":
                {
                    EndCell();
                    _row ??= new List<List<MarkdownSpan>>();
                    if (name == "th")
                        _styles.Push(SpanStyle.Bold);
                    break;
                }
                case "b" or "strong":
                {
                    _styles.Push(SpanStyle.Bold);
                    break;
                }
                case "i" or "em" or "cite":
                {
                    _styles.Push(SpanStyle.Italic);
                    break;
                }
                case "code" or "kbd" or "samp" or "tt":
                {
                    if (!_inCode)
                        _styles.Push(SpanStyle.Code);
                    break;
                }
                case "s" or "del" or "strike":
                {
                    _styles.Push(SpanStyle.Strike);
                    break;
                }
                case "a":
                {
                    _links.Push(Attribute(attributes, "href"));
                    break;
                }
                case "img":
                {
                    string alt = Attribute(attributes, "alt") ?? "image";
                    double? width = double.TryParse(Attribute(attributes, "width"), NumberStyles.Float,
                                                    CultureInfo.InvariantCulture, out double pixels) ? pixels : null;
                    _spans.Add(new MarkdownSpan(alt, SpanStyle.Image, Attribute(attributes, "src"), width));
                    _source.Append("[img:").Append(alt).Append(']');
                    break;
                }
                default:
                {
                    if (BlockTags.Contains(name))
                        Flush();
                    break;
                }
            }
        }

        public void Close(string name)
        {
            switch (name)
            {
                case "h1" or "h2" or "h3" or "h4" or "h5" or "h6" or "li":
                {
                    Flush();
                    break;
                }
                case "pre":
                {
                    _inCode = false;
                    string code = _code.ToString().Trim('\n', '\r');
                    if (code.Length > 0)
                        Blocks.Add(new MarkdownBlock { Kind = BlockKind.Code, Text = code, Source = code });
                    break;
                }
                case "blockquote":
                {
                    Flush();
                    _quoteDepth = Math.Max(0, _quoteDepth - 1);
                    break;
                }
                case "ul" or "ol":
                {
                    Flush();
                    if (_lists.Count > 0)
                        _lists.Pop();
                    break;
                }
                case "td" or "th":
                {
                    EndCell();
                    if (name == "th" && _styles.Count > 0)
                        _styles.Pop();
                    break;
                }
                case "tr":
                {
                    EndCell();
                    if (_row is { Count: > 0 })
                        _table?.Add(_row);
                    _row = null;
                    break;
                }
                case "table":
                {
                    EndCell();
                    if (_row is { Count: > 0 })
                        _table?.Add(_row);
                    _row = null;
                    if (_table is { Count: > 0 })
                        Blocks.Add(new MarkdownBlock
                        {
                            Kind = BlockKind.Table,
                            Rows = _table,
                            Source = string.Join("\n", _table.Select(row => string.Join("|", row.Select(
                                cell => string.Concat(cell.Select(span => span.Text))))))
                        });
                    _table = null;
                    break;
                }
                case "b" or "strong" or "i" or "em" or "cite" or "s" or "del" or "strike":
                {
                    if (_styles.Count > 0)
                        _styles.Pop();
                    break;
                }
                case "code" or "kbd" or "samp" or "tt":
                {
                    if (!_inCode && _styles.Count > 0)
                        _styles.Pop();
                    break;
                }
                case "a":
                {
                    if (_links.Count > 0)
                        _links.Pop();
                    break;
                }
                default:
                {
                    if (BlockTags.Contains(name))
                        Flush();
                    break;
                }
            }
        }

        // Inside a table, text collected so far goes into the current cell
        private void EndCell()
        {
            if (_table == null || _spans.Count == 0)
                return;
            _row ??= new List<List<MarkdownSpan>>();
            _row.Add(Trimmed());
            _spans.Clear();
            _source.Clear();
        }

        // Ends the block being read, if it has any text
        public void Flush()
        {
            if (_table != null)
            {
                EndCell();
                return;
            }

            List<MarkdownSpan> spans = Trimmed();
            if (spans.Count > 0)
            {
                BlockKind kind = _kind == BlockKind.Paragraph && _quoteDepth > 0 ? BlockKind.Quote : _kind;
                Blocks.Add(new MarkdownBlock
                {
                    Kind = kind,
                    Level = kind == BlockKind.Quote ? _quoteDepth : _level,
                    Marker = _marker,
                    Spans = spans,
                    Source = kind + ":" + _source.ToString().Trim()
                });
            }

            _spans.Clear();
            _source.Clear();
            _kind = BlockKind.Paragraph;
            _level = 0;
            _marker = null;
        }

        // The spans without the space or line break at their very end
        private List<MarkdownSpan> Trimmed()
        {
            List<MarkdownSpan> spans = _spans.Where(span => span.Text.Length > 0).ToList();
            while (spans.Count > 0 && spans[^1].Text.Trim().Length == 0)
                spans.RemoveAt(spans.Count - 1);
            if (spans.Count > 0)
                spans[^1] = spans[^1] with { Text = spans[^1].Text.TrimEnd() };
            return spans;
        }
    }
}
