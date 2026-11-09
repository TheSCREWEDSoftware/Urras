using System.Reflection;

namespace Urras.Core.Tests;

// Every public static method of a class ending in "Tests" is a test; both runners find them here
public static class TestCatalog
{
    public static IEnumerable<MethodInfo> All()
    {
        return typeof(TestCatalog).Assembly.GetTypes()
                                  .Where(type => type.IsClass && type.IsAbstract && type.IsSealed && type.IsPublic &&
                                                 type.Name.EndsWith("Tests", StringComparison.Ordinal))
                                  .OrderBy(type => type.Name, StringComparer.Ordinal)
                                  .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Static))
                                  .Where(method => method.GetParameters().Length == 0 &&
                                                   (method.ReturnType == typeof(Task) ||
                                                    method.ReturnType == typeof(void)));
    }

    public static string Name(MethodInfo method)
    {
        return method.DeclaringType!.Name + "." + method.Name;
    }

    public static MethodInfo Find(string name)
    {
        return All().First(method => Name(method) == name);
    }

    // Runs one test; failures come out as the exception the test threw
    public static async Task RunAsync(MethodInfo method)
    {
        TestEnvironment.Prepare();
        try
        {
            object? result = method.Invoke(null, null);
            if (result is Task task)
                await task;
        }
        catch (TargetInvocationException exception) when (exception.InnerException != null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
        }
    }
}
