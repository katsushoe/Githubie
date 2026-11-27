namespace Githubie.Infrastructure.Configuration;

/// <summary>OSの現在ユーザー向け起動エントリの読み書きです。ユーザーの好みはJSONに別途保存します。</summary>
public interface IUserStartupRegistration
{
    string? Read();
    void Write(string? command);
}
