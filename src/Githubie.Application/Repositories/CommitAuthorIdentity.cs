namespace Githubie.Application.Repositories;

public static class CommitAuthorIdentity
{
    public static bool IsValid(string? name, string? email) =>
        !string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(email)
        && name.Length <= 200 && email.Length <= 320
        && !name.Any(char.IsControl) && !email.Any(char.IsControl)
        && !email.Any(char.IsWhiteSpace) && email.Contains('@', StringComparison.Ordinal);
}
