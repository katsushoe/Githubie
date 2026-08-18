using System.Text.RegularExpressions;

namespace Githubie.Application.Repositories;

/// <summary>
/// Githubie内部で使うRepository ID（英数字と`._-`のみ、最大128文字）を検証します。
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

    [GeneratedRegex("^[A-Za-z0-9._-]+$")]
    private static partial Regex IdPattern();
}
