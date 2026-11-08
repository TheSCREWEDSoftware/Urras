namespace Urras.Standards;

// One broken rule: where, which rule, and what to do about it. Line 0 means the whole file or commit.
public sealed record Violation(string Where, int Line, string Rule, string Message)
{
    public override string ToString()
    {
        return Where + (Line > 0 ? ":" + Line : string.Empty) + ": [" + Rule + "] " + Message;
    }
}
