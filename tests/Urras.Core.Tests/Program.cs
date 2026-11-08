using System.Reflection;
using Urras.Core.Tests;

// Runs every test with no package dependencies; tests/Urras.Core.Tests.XUnit runs the same ones with coverage
int passed = 0;
int failed = 0;

foreach (MethodInfo method in TestCatalog.All())
{
    string name = TestCatalog.Name(method);
    try
    {
        await TestCatalog.RunAsync(method);
        Console.WriteLine("PASS " + name);
        ++passed;
    }
    catch (Exception exception)
    {
        Console.WriteLine("FAIL " + name + ": " + exception.Message);
        ++failed;
    }
}

Console.WriteLine($"{passed} passed, {failed} failed");
return failed == 0 ? 0 : 1;
