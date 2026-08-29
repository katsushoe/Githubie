using System.Text.RegularExpressions;

namespace Githubie.Application.Repositories;

/// <summary>
/// Githubie内部で使うRepository ID（小文字ASCII英字で始まる小文字ASCII英数字、最大128文字）を検証します。
/// </summary>
public static partial class RepositoryId
{
    private const int MaxLength = 128;

    public static bool IsValid(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > MaxLength)
        {
            return false;
        }

        return IdPattern().IsMatch(value);
    }

    public static bool TryNormalize(string? value, out string normalized)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > MaxLength || !LookupPattern().IsMatch(value))
        {
            normalized = string.Empty;
            return false;
        }

        normalized = value.ToLowerInvariant();
        return true;
    }

    public static bool TryNormalizeLegacy(string? value, out string normalized)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > MaxLength || !LegacyPattern().IsMatch(value))
        {
            normalized = string.Empty;
            return false;
        }

        normalized = string.Concat(value.Where(character => character is not ('.' or '_' or '-'))).ToLowerInvariant();
        return IsValid(normalized);
    }

    [GeneratedRegex("^[a-z][a-z0-9]*$")]
    private static partial Regex IdPattern();

    [GeneratedRegex("^[A-Za-z][A-Za-z0-9]*$")]
    private static partial Regex LookupPattern();

    [GeneratedRegex("^[A-Za-z0-9._-]+$")]
    private static partial Regex LegacyPattern();
}
