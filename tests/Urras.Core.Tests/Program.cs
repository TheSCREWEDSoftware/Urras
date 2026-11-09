using System.Reflection;
using Urras.Core.Tests;

// Runs every test with no package dependencies; tests/Urras.Core.Tests.XUnit runs the same ones with coverage
int passed = 0;
int failed = 0;
List<(string Position, string Line)> failures = new List<(string, string)>();
List<MethodInfo> tests = TestCatalog.All().ToList();
int width = tests.Count.ToString().Length;

foreach (MethodInfo method in tests)
{
    // [007/152]: how far along the run is
    string name = TestCatalog.Name(method);
    string position = "[" + (passed + failed + 1).ToString().PadLeft(width, '0') + "/" + tests.Count + "] ";
    try
    {
        await TestCatalog.RunAsync(method);
        Write(position, "PASS", ConsoleColor.Green, name);
        ++passed;
    }
    catch (Exception exception)
    {
        string line = name + ": " + OneLine(exception.Message);
        Write(position, "FAIL", ConsoleColor.Red, line);
        failures.Add((position, line));
        ++failed;
    }
}

// The failures again at the end, so they don't have to be found in the list
if (failures.Count > 0)
{
    Console.WriteLine();
    foreach ((string position, string line) in failures)
        Write(position, "FAIL", ConsoleColor.Red, line);
}

Console.WriteLine();
Console.ForegroundColor = failed == 0 ? ConsoleColor.Green : ConsoleColor.Red;
Console.WriteLine($"{passed} passed, {failed} failed");
Console.ResetColor();
return failed == 0 ? 0 : 1;

static void Write(string position, string label, ConsoleColor colour, string text)
{
    Console.Write(position);
    Console.ForegroundColor = colour;
    Console.Write(label);
    Console.ResetColor();
    Console.WriteLine(" " + text);
}

// Messages that span lines (a stack, Git's output) are kept on one line
static string OneLine(string message)
{
    string[] lines = message.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    return string.Join(" | ", lines);
}
