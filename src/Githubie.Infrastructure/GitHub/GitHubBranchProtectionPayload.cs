using System.Text.Json.Nodes;
using Githubie.Application.GitHub;

namespace Githubie.Infrastructure.GitHub;

/// <summary>既存のGitHub保護設定から、安全に再現可能なPUT本文を構成します。</summary>
internal static class GitHubBranchProtectionPayload
{
    private static readonly string[] BooleanFields =
    ["enforce_admins", "required_linear_history", "allow_force_pushes", "allow_deletions",
     "block_creations", "required_conversation_resolution", "lock_branch", "allow_fork_syncing"];

    private static readonly HashSet<string> KnownFields = new(StringComparer.Ordinal)
    {
        "url", "required_status_checks", "required_pull_request_reviews", "restrictions", "required_signatures",
        "enforce_admins", "required_linear_history", "allow_force_pushes", "allow_deletions",
        "block_creations", "required_conversation_resolution", "lock_branch", "allow_fork_syncing",
    };

    /// <summary>GET応答を、既存設定を保全できるPUT形式に正規化します。</summary>
    public static bool TryNormalize(JsonObject current, out JsonObject? payload)
    {
        payload = ExistingRule(current);
        return payload is not null;
    }

    /// <summary>変換できない設定は、部分的な本文を返さずに拒否します。</summary>
    public static bool TryBuild(JsonObject? current, GitHubBranchProtectionEdit edit, out JsonObject? payload)
    {
        payload = null;
        if (!edit.IsValid()) return false;
        var result = current is null ? NewRule(edit) : ExistingRule(current);
        if (result is null) return false;

        if (edit.RequirePullRequest == false) result["required_pull_request_reviews"] = null;
        if (edit.RequirePullRequest == true && result["required_pull_request_reviews"] is null)
            result["required_pull_request_reviews"] = new JsonObject
            {
                ["dismiss_stale_reviews"] = false,
                ["require_code_owner_reviews"] = false,
                ["required_approving_review_count"] = 0,
                ["require_last_push_approval"] = false,
            };
        if (edit.RequiredApprovingReviews is int count)
        {
            if (result["required_pull_request_reviews"] is not JsonObject reviews) return false;
            reviews["required_approving_review_count"] = count;
        }

        if (edit.RequireStatusChecks == false) result["required_status_checks"] = null;
        if (edit.RequireStatusChecks == true && result["required_status_checks"] is null)
            result["required_status_checks"] = new JsonObject { ["strict"] = false, ["contexts"] = new JsonArray() };
        if (edit.StatusCheckContexts is not null || edit.StrictStatusChecks is not null)
        {
            if (result["required_status_checks"] is not JsonObject checks) return false;
            if (edit.StatusCheckContexts is not null)
            {
                // 名前だけの入力では、特定Appに固定された既存チェックを安全に再現できません。
                if (checks["checks"] is JsonArray existingChecks
                    && existingChecks.Any(item => item?["app_id"] is JsonNode app
                        && app.ToJsonString() != "-1")) return false;
                checks["contexts"] = new JsonArray(edit.StatusCheckContexts.Select(value => (JsonNode?)JsonValue.Create(value)).ToArray());
                checks.Remove("checks");
            }
            if (edit.StrictStatusChecks is bool strict) checks["strict"] = strict;
        }

        if (edit.EnforceAdmins is bool admins) result["enforce_admins"] = admins;
        if (edit.AllowForcePushes is bool force) result["allow_force_pushes"] = force;
        if (edit.AllowDeletions is bool deletions) result["allow_deletions"] = deletions;
        payload = result;
        return true;
    }

    private static JsonObject? NewRule(GitHubBranchProtectionEdit edit)
    {
        // 新規作成時には、必須セキュリティ項目の無指定を既定値として推測しません。
        if (edit.RequirePullRequest is null || edit.AllowForcePushes is null || edit.AllowDeletions is null
            || edit.EnforceAdmins is null || edit.RequireStatusChecks is null
            || (edit.RequirePullRequest == true && edit.RequiredApprovingReviews is null))
            return null;
        return new JsonObject
        {
            ["required_status_checks"] = null,
            ["required_pull_request_reviews"] = null,
            ["restrictions"] = null,
            ["enforce_admins"] = false,
            ["required_linear_history"] = false,
            ["allow_force_pushes"] = false,
            ["allow_deletions"] = false,
            ["block_creations"] = false,
            ["required_conversation_resolution"] = false,
            ["lock_branch"] = false,
            ["allow_fork_syncing"] = false,
        };
    }

    private static JsonObject? ExistingRule(JsonObject current)
    {
        if (current.Any(pair => !KnownFields.Contains(pair.Key))) return null;
        if (!current.TryGetPropertyValue("required_status_checks", out var status)
            || !current.TryGetPropertyValue("required_pull_request_reviews", out var reviews)
            || !current.TryGetPropertyValue("restrictions", out var restrictions)) return null;
        if (current.TryGetPropertyValue("required_signatures", out var signatures) && signatures is not null
            && (!ReadEnabled(signatures, out var signed) || signed)) return null;

        var result = new JsonObject();
        foreach (var field in BooleanFields)
        {
            if (!ReadEnabled(current[field], out var value)) return null;
            result[field] = value;
        }
        if (!ConvertStatus(status, out var convertedStatus)
            || !ConvertReviews(reviews, out var convertedReviews)
            || !ConvertSubjects(restrictions, out var convertedRestrictions)) return null;
        result["required_status_checks"] = convertedStatus;
        result["required_pull_request_reviews"] = convertedReviews;
        result["restrictions"] = convertedRestrictions;
        return result;
    }

    private static bool ConvertStatus(JsonNode? node, out JsonNode? result)
    {
        result = null;
        if (node is null) return true;
        if (node is not JsonObject status || !OnlyKnown(status, "url", "contexts_url", "checks_url", "enforcement_level", "strict", "contexts", "checks")
            || !ReadBoolean(status["strict"], out var strict)
            || !ReadStrings(status["contexts"], out var contexts)) return false;
        var output = new JsonObject { ["strict"] = strict, ["contexts"] = contexts };
        if (status.TryGetPropertyValue("checks", out var checksNode) && checksNode is not null)
        {
            if (checksNode is not JsonArray checks) return false;
            var outputChecks = new JsonArray();
            foreach (var item in checks)
            {
                if (item is not JsonObject check || !OnlyKnown(check, "context", "app_id")
                    || !ReadString(check["context"], out var context)) return false;
                var outputCheck = new JsonObject { ["context"] = context };
                if (check.TryGetPropertyValue("app_id", out var app) && app is not null)
                {
                    if (app is not JsonValue id) return false;
                    if (!id.TryGetValue<long>(out var appId))
                    {
                        if (!id.TryGetValue<int>(out var smallId)) return false;
                        appId = smallId;
                    }
                    outputCheck["app_id"] = appId;
                }
                outputChecks.Add(outputCheck);
            }
            output["checks"] = outputChecks;
        }
        result = output;
        return true;
    }

    private static bool ConvertReviews(JsonNode? node, out JsonNode? result)
    {
        result = null;
        if (node is null) return true;
        if (node is not JsonObject reviews || !OnlyKnown(reviews, "url", "dismissal_restrictions_url",
                "dismiss_stale_reviews", "require_code_owner_reviews", "required_approving_review_count",
                "require_last_push_approval", "dismissal_restrictions", "bypass_pull_request_allowances")
            || reviews["required_approving_review_count"] is not JsonValue countNode
            || !countNode.TryGetValue<int>(out var count) || count is < 0 or > 6) return false;
        var output = new JsonObject { ["required_approving_review_count"] = count };
        foreach (var name in new[] { "dismiss_stale_reviews", "require_code_owner_reviews", "require_last_push_approval" })
        {
            if (!ReadBoolean(reviews[name], out var enabled)) return false;
            output[name] = enabled;
        }
        foreach (var name in new[] { "dismissal_restrictions", "bypass_pull_request_allowances" })
        {
            if (!reviews.TryGetPropertyValue(name, out var subjectNode)) continue;
            if (!ConvertSubjects(subjectNode, out var subjects)) return false;
            if (subjects is not null) output[name] = subjects;
        }
        result = output;
        return true;
    }

    private static bool ConvertSubjects(JsonNode? node, out JsonNode? result)
    {
        result = null;
        if (node is null) return true;
        if (node is not JsonObject subjects || !OnlyKnown(subjects, "url", "users_url", "teams_url", "apps_url", "users", "teams", "apps")) return false;
        var output = new JsonObject();
        foreach (var (name, key) in new[] { ("users", "login"), ("teams", "slug"), ("apps", "slug") })
        {
            if (!subjects.TryGetPropertyValue(name, out var input))
            {
                if (name == "apps") continue;
                return false;
            }
            if (input is not JsonArray values) return false;
            var strings = new JsonArray();
            foreach (var value in values)
            {
                var item = value is JsonObject obj ? obj[key] : value;
                if (!ReadString(item, out var text)) return false;
                strings.Add(text);
            }
            output[name] = strings;
        }
        result = output;
        return true;
    }

    private static bool ReadEnabled(JsonNode? node, out bool value)
    {
        value = false;
        return node is JsonObject wrapper
            ? OnlyKnown(wrapper, "url", "enabled") && ReadBoolean(wrapper["enabled"], out value)
            : ReadBoolean(node, out value);
    }

    private static bool OnlyKnown(JsonObject value, params string[] names) =>
        value.All(pair => names.Contains(pair.Key, StringComparer.Ordinal));

    private static bool ReadBoolean(JsonNode? node, out bool value)
    {
        value = false;
        return node is JsonValue scalar && scalar.TryGetValue(out value);
    }

    private static bool ReadString(JsonNode? node, out string value)
    {
        value = string.Empty;
        if (node is not JsonValue scalar || !scalar.TryGetValue<string>(out var text) || string.IsNullOrWhiteSpace(text)) return false;
        value = text;
        return true;
    }

    private static bool ReadStrings(JsonNode? node, out JsonArray result)
    {
        result = new JsonArray();
        if (node is not JsonArray source) return false;
        foreach (var item in source)
        {
            if (!ReadString(item, out var text)) return false;
            result.Add(text);
        }
        return true;
    }
}
