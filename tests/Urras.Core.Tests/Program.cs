using System.Reflection;
using Urras.Core.Tests;

// Minimal test runner with no package dependencies: every public static async method of a test class is a test
Type[] testClasses =
{
    typeof(ParserTests), typeof(DiffParserTests), typeof(LayoutTests), typeof(BasicsTests), typeof(DiffViewTests),
    typeof(ActionsTests), typeof(HistoryTests), typeof(SyntaxTests), typeof(GraphTests), typeof(LineStagingTests),
    typeof(RebaseTests), typeof(ConflictTests), typeof(BisectTests), typeof(ModuleTests), typeof(LfsTests),
    typeof(RepositoryTests), typeof(SettingsTests)
};
int passed = 0;
int failed = 0;

foreach (Type testClass in testClasses)
{
    foreach (MethodInfo method in testClass.GetMethods(BindingFlags.Public | BindingFlags.Static))
    {
        if (method.ReturnType != typeof(Task) && method.ReturnType != typeof(void))
            continue;

        string name = testClass.Name + "." + method.Name;
        try
        {
            object? result = method.Invoke(null, null);
            if (result is Task task)
                await task;

            Console.WriteLine("PASS " + name);
            ++passed;
        }
        catch (Exception exception)
        {
            Exception actual = exception is TargetInvocationException { InnerException: not null }
                ? exception.InnerException
                : exception;
            Console.WriteLine("FAIL " + name + ": " + actual.Message);
            ++failed;
        }
    }
}

Console.WriteLine($"{passed} passed, {failed} failed");
return failed == 0 ? 0 : 1;
