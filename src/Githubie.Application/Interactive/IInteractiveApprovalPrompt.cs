namespace Githubie.Application.Interactive;

/// <summary>対話セッションの人間へ危険な操作の承認を要求します。</summary>
public interface IInteractiveApprovalPrompt
{
    Task<ApprovalPromptOutcome> RequestApprovalAsync(
        ApprovalPromptRequest request,
        TimeSpan timeout,
        CancellationToken cancellationToken);
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
