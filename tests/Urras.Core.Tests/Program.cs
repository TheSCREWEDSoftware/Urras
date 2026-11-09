using System.Reflection;
using Urras.Core.Tests;

// Runs every test with no package dependencies; tests/Urras.Core.Tests.XUnit runs the same ones with coverage
int passed = 0;
int failed = 0;
List<string> failures = new List<string>();

foreach (MethodInfo method in TestCatalog.All())
{
    string name = TestCatalog.Name(method);
    try
    {
        await TestCatalog.RunAsync(method);
        Write("PASS", ConsoleColor.Green, name);
        ++passed;
    }
    catch (Exception exception)
    {
        string line = name + ": " + OneLine(exception.Message);
        Write("FAIL", ConsoleColor.Red, line);
        failures.Add(line);
        ++failed;
    }
}

// The failures again at the end, so they don't have to be found in the list
if (failures.Count > 0)
{
    Console.WriteLine();
    foreach (string failure in failures)
        Write("FAIL", ConsoleColor.Red, failure);
}

Console.WriteLine();
Console.ForegroundColor = failed == 0 ? ConsoleColor.Green : ConsoleColor.Red;
Console.WriteLine($"{passed} passed, {failed} failed");
Console.ResetColor();
return failed == 0 ? 0 : 1;

static void Write(string label, ConsoleColor colour, string text)
{
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
