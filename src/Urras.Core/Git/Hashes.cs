namespace Urras.Core.Git;

public static class Hashes
{
    // Characters shown for a commit, matching git's usual abbreviation
    public const int SHORT_LENGTH = 7;

    // A full SHA-1 commit id; SHA-256 repositories use 64
    public const int SHA1_LENGTH = 40;

    public static string Short(string hash)
    {
        return hash.Length > SHORT_LENGTH ? hash[..SHORT_LENGTH] : hash;
    }
}
