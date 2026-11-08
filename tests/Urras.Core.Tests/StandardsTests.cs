using System.Text;
using Urras.Standards;

namespace Urras.Core.Tests;

public static class StandardsTests
{
    private static List<string> Rules(string path, string text)
    {
        return FileRules.Check(path, Encoding.UTF8.GetBytes(text)).Select(violation => violation.Rule).ToList();
    }

    public static void ChecksFileFormat()
    {
        Assert.Equal(0, Rules("a.cs", "namespace A;\n").Count, "a clean file");
        Assert.True(Rules("a.cs", "namespace A;").Contains("newline"), "missing final newline");
        Assert.True(Rules("a.cs", "namespace A;\r\n").Contains("line-endings"), "CRLF in a C# file");
        Assert.True(!Rules("a.bat", "@echo off\r\n").Contains("line-endings"), "CRLF in a batch file");
        Assert.True(Rules("a.bat", "@echo off\n").Contains("line-endings"), "LF in a batch file");
        Assert.True(Rules("a.md", "x \n").Contains("trailing-whitespace"), "trailing space");
        Assert.True(Rules("a.md", "\tx\n").Contains("tabs"), "tab");
        Assert.True(Rules("a.md", new string('x', 121) + "\n").Contains("line-length"), "too long");
        Assert.True(Rules("a.md", "Done \u2705\n").Contains("emoji"), "emoji");
        Assert.True(FileRules.Check("a.cs", new byte[] { 0xEF, 0xBB, 0xBF, (byte)'\n' })
                             .Any(violation => violation.Rule == "encoding"), "BOM");
        Assert.Equal(0, Rules("LICENSE", new string('x', 200)).Count, "the license is left as published");
        Assert.Equal(0, Rules("src/obj/x.cs", "\t").Count, "build output is skipped");
    }

    public static void ChecksCSharpRules()
    {
        Assert.True(Rules("a.cs", "namespace A;\nvar x = 1;\n").Contains("var"), "var");
        Assert.True(!Rules("a.cs", "namespace A;\nstring s = \"var x = 1\";\n").Contains("var"), "var in a string");
        Assert.True(Rules("a.cs", "namespace A;\nif ( x )\n").Contains("parentheses"), "spaces in parentheses");
        Assert.True(Rules("a.cs", "namespace A;\nprivate int count;\n").Contains("naming"), "private field");
        Assert.True(!Rules("a.cs", "namespace A;\nprivate int _count;\n").Contains("naming"), "_field is fine");
        Assert.True(Rules("a.cs", "namespace A;\nconst int maxCount = 3;\n").Contains("naming"), "constant name");
        Assert.True(Rules("a.cs", "namespace A;\n// one\n// two\n").Contains("comments"), "two-line comment");
        Assert.True(Rules("a.cs", "namespace A;\n// see https://example.org\n").Contains("comments"), "link");
        Assert.True(Rules("a.cs", "namespace A\n{\n}\n").Contains("namespace"), "block namespace");
        Assert.True(Rules("a.cs", "using Avalonia;\nusing System;\nnamespace A;\n").Contains("usings"), "order");

        const string NO_DEFAULT = "namespace A;\nswitch (x)\n{\n    case 1:\n        break;\n}\n";
        Assert.True(Rules("a.cs", NO_DEFAULT).Contains("switch"), "switch without default");
        const string NO_BRACES = "namespace A;\nswitch (x)\n{\n    case 1:\n        A();\n        B();\n" +
                                 "        break;\n    default:\n        break;\n}\n";
        Assert.True(Rules("a.cs", NO_BRACES).Contains("switch"), "case with several statements");
        const string GOOD = "namespace A;\nswitch (x)\n{\n    case 1:\n    {\n        A();\n        B();\n" +
                            "        break;\n    }\n    default:\n        break;\n}\n";
        Assert.True(!Rules("a.cs", GOOD).Contains("switch"), "braced case and a default");
    }

    public static void ChecksCommitMessages()
    {
        Assert.Equal(0, CommitRules.Check("c", "fix(Git/Status): Paths with spaces work\n\nWhy it matters.\n\n" +
                                                "Co-Authored-By: Someone With A Long Name <someone@example.com>\n")
                                   .Count, "a good message, trailers may be long");
        Assert.True(CommitRules.Check("c", "Add a thing").Any(), "no type and scope");
        Assert.True(CommitRules.Check("c", "feature(UI): Add a thing").Any(), "unknown type");
        Assert.True(CommitRules.Check("c", "feat(Window): Add a thing").Any(), "unknown scope");
        Assert.True(CommitRules.Check("c", "feat(UI): add a thing").Any(), "lower-case summary");
        Assert.True(CommitRules.Check("c", "feat(UI): Add a thing.").Any(), "full stop");
        Assert.True(CommitRules.Check("c", "feat(UI): Add a thing\nBody right away").Any(), "no blank line");
        Assert.True(CommitRules.Check("c", "feat(UI): Add\n\nClaude-Session: x").Any(), "session link");
        Assert.True(CommitRules.Check("c", "feat(UI): Add\n\n" + new string('x', 73)).Any(), "long body line");
    }

    // Like Keira3's checks: the repository itself must follow the standards
    public static void RepositoryFollowsTheStandards()
    {
        string? root = AppContext.BaseDirectory;
        while (root != null && !File.Exists(Path.Combine(root, "CODING_STANDARDS.md")))
            root = Path.GetDirectoryName(root);
        if (root == null)
            return;

        List<Violation> violations = new List<Violation>();
        foreach (string file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(root, file).Replace('\\', '/');
            if (relative.StartsWith(".git/") || relative.StartsWith(".agents/") || !FileRules.IsChecked(relative))
                continue;
            violations.AddRange(FileRules.Check(relative, File.ReadAllBytes(file)));
        }

        Assert.True(violations.Count == 0, "every file follows CODING_STANDARDS.md:\n" +
                                           string.Join("\n", violations.Take(20)));
    }
}
