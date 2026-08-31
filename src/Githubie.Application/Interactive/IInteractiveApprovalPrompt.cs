namespace Githubie.Application.Interactive;

/// <summary>対話セッションの人間へ危険な操作の承認を要求します。</summary>
public interface IInteractiveApprovalPrompt
{
    Task<ApprovalPromptOutcome> RequestApprovalAsync(
        ApprovalPromptRequest request,
        TimeSpan timeout,
        CancellationToken cancellationToken);
}

/// <summary>対話セッションの人間から秘密値を安全なPipe経由で受け取ります。</summary>
public interface IInteractiveTokenPrompt
{
    Task<InteractiveTokenPromptResult> RequestTokenAsync(
        TokenPromptRequest request,
        TimeSpan timeout,
        CancellationToken cancellationToken);
}

/// <summary>Token画面へ表示する秘密値を含まないRepository情報です。</summary>
public sealed record TokenPromptRequest(string ProjectName, string RepositoryUrl);

public enum InteractiveTokenPromptOutcome
{
    Accepted,
    Skipped,
    TimedOut,
    NoInteractiveSession,
    LaunchFailed,
    ProtocolError,
}

/// <summary>Token本体は利用後に呼び出し側で消去します。</summary>
public sealed record InteractiveTokenPromptResult(
    InteractiveTokenPromptOutcome Outcome,
    char[]? Token)
{
    public static InteractiveTokenPromptResult Accepted(char[] token) =>
        new(InteractiveTokenPromptOutcome.Accepted, token);

    public static InteractiveTokenPromptResult Failure(InteractiveTokenPromptOutcome outcome) =>
        new(outcome, null);
}

public enum ApprovalOutcome
{
    Approved,
    Denied,
    TimedOut,
    NoInteractiveSession,
    LaunchFailed,
    ProtocolError,
}

/// <summary>承認画面へ表示する秘密値を含まない要求です。</summary>
public sealed record ApprovalPromptRequest(string Title, string Summary, IReadOnlyList<string> Details);

public sealed record ApprovalPromptOutcome(ApprovalOutcome Outcome)
{
    public static ApprovalPromptOutcome Approved() => new(ApprovalOutcome.Approved);
    public static ApprovalPromptOutcome Denied() => new(ApprovalOutcome.Denied);
    public static ApprovalPromptOutcome Failure(ApprovalOutcome outcome) => new(outcome);
}
