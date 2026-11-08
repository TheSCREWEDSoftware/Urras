using Urras.Core.Tests;
using Xunit;

namespace Urras.Core.Tests.XUnit;

// One xUnit case per core test, found by TestCatalog, so there is a single list of tests
public sealed class CoreTests
{
    public static IEnumerable<object[]> Cases()
    {
        return TestCatalog.All().Select(method => new object[] { TestCatalog.Name(method) });
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public Task Run(string test)
    {
        return TestCatalog.RunAsync(TestCatalog.Find(test));
    }
}
