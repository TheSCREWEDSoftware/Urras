using Urras.Core.Git;

namespace Urras.Core.Tests;

public static class CoAuthorTests
{
    public static void ParsesPeopleAndTrailers()
    {
        Assert.Equal(new CoAuthor("Ana Silva", "ana@example.com"), CoAuthor.Parse(" Ana Silva <ana@example.com> "),
                     "name and email");
        Assert.Equal("bob", CoAuthor.Parse("<bob@example.com>")!.Name, "name from the email");
        Assert.True(CoAuthor.Parse("just a name") == null, "no email");
        Assert.True(CoAuthor.Parse("Ana <not-an-email>") == null, "bad email");

        CoAuthor github = CoAuthor.FromGitHub(123, "Nyeriah", null);
        Assert.Equal("123+Nyeriah@users.noreply.github.com", github.Email, "GitHub private email");
        Assert.Equal("@Nyeriah", github.Label, "shown as @username");
        Assert.Equal("Co-authored-by: Nyeriah <123+Nyeriah@users.noreply.github.com>", github.Trailer, "trailer");
    }

    public static void AppendsAndExtractsTrailers()
    {
        List<CoAuthor> people = new()
        {
            new CoAuthor("Ana", "ana@example.com"), new CoAuthor("Ana again", "ANA@example.com"),
            new CoAuthor("Bob", "bob@example.com")
        };
        string message = CoAuthor.Append("Fix the sonar\n\nDetails here.\n", people);
        Assert.Equal("Fix the sonar\n\nDetails here.\n\nCo-authored-by: Ana <ana@example.com>\n" +
                     "Co-authored-by: Bob <bob@example.com>", message, "one trailer each, after a blank line");
        Assert.Equal("Summary", CoAuthor.Append("Summary", Array.Empty<CoAuthor>()), "nothing to add");

        (string text, List<CoAuthor> found) = CoAuthor.Extract(message.Replace("\n", "\r\n"));
        Assert.Equal("Fix the sonar\n\nDetails here.", text, "trailers removed");
        Assert.Equal(2, found.Count, "both found");
        Assert.Equal("Bob", found[1].Name, "in order");
    }

    public static async Task SuggestsPeopleFromTheHistory()
    {
        using TempRepository temp = await TempRepository.CreateAsync();
        temp.Write("a.txt", "a\n");
        await temp.CommitAllAsync("First");
        temp.Write("b.txt", "b\n");
        Assert.Success(await GitRunner.RunAsync(temp.Path, "add", "-A"), "add");
        Assert.Success(await GitRunner.RunAsync(temp.Path, "-c", "user.name=Ana", "-c", "user.email=ana@example.com",
                                                "commit", "-q", "-m", "By Ana"), "commit as Ana");

        List<CoAuthor> people = await temp.Repository.GetKnownPeopleAsync();
        Assert.True(people.Any(person => person.Email == "ana@example.com" && person.Name == "Ana"), "Ana suggested");
        Assert.Equal(people.Count, people.Select(person => person.Email.ToLowerInvariant()).Distinct().Count(),
                     "each person once");
    }
}
