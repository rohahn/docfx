// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Docfx.Plugins;

#nullable enable

namespace Docfx.Common.Git;

public record GitSource(string Repo, string Branch, string Path, int Line);

public static class GitUtility
{
    record Repo(string path, string url, string branch);

    private static readonly ConcurrentDictionary<string, Repo?> s_cache = new();

    private static readonly string? s_branch =
        Env("DOCFX_SOURCE_BRANCH_NAME") ??
        Env("GITHUB_REF_NAME") ??  // GitHub Actions
        Env("APPVEYOR_REPO_BRANCH") ?? // AppVeyor
        Env("Git_Branch") ?? // Team City
        Env("CI_BUILD_REF_NAME") ?? // GitLab CI
        Env("GIT_LOCAL_BRANCH") ??// Jenkins
        Env("GIT_BRANCH") ?? // Jenkins
        Env("BUILD_SOURCEBRANCHNAME"); // VSO Agent

    private static string? Env(string name) => Environment.GetEnvironmentVariable(name) is { } value && !string.IsNullOrEmpty(value) ? value : null;

    public static GitDetail? TryGetFileDetail(string filePath)
    {
        if (EnvironmentContext.GitFeaturesDisabled)
            return null;

        var repo = GetRepoInfo(Path.GetDirectoryName(filePath));
        if (repo is null)
            return null;

        return new()
        {
            Repo = repo.url,
            Branch = repo.branch,
            Path = Path.GetRelativePath(repo.path, filePath).Replace('\\', '/'),
        };
    }

    public static string? RawContentUrlToContentUrl(string rawUrl)
    {
        // GitHub
        return Regex.Replace(
            rawUrl,
            @"^https://raw\.githubusercontent\.com/([^/]+)/([^/]+)/([^/]+)/(.+)$",
            string.IsNullOrEmpty(s_branch) ? "https://github.com/$1/$2/blob/$3/$4" : $"https://github.com/$1/$2/blob/{s_branch}/$4");
    }

    public static string? GetSourceUrl(GitSource source)
    {
        var repo = source.Repo.StartsWith("git") ? GitUrlToHttps(source.Repo) : source.Repo;
        repo = repo.TrimEnd('/').TrimEnd(".git");

        if (!Uri.TryCreate(repo, UriKind.Absolute, out var url))
            return null;

        var path = source.Path.Replace('\\', '/');

        return url.Host switch
        {
            "github.com" => $"https://github.com{url.AbsolutePath}/blob/{source.Branch}/{path}{(source.Line > 0 ? $"#L{source.Line}" : null)}",
            "bitbucket.org" => $"https://bitbucket.org{url.AbsolutePath}/src/{source.Branch}/{path}{(source.Line > 0 ? $"#lines-{source.Line}" : null)}",
            _ when url.Host.EndsWith(".visualstudio.com") || url.Host == "dev.azure.com" =>
                $"https://{url.Host}{url.AbsolutePath}?path={path}&version={(IsCommit(source.Branch) ? "GC" : "GB")}{source.Branch}{(source.Line > 0 ? $"&line={source.Line}" : null)}",
            _ => null,
        };

        static bool IsCommit(string branch)
        {
            return branch.Length == 40 && branch.All(char.IsLetterOrDigit);
        }

        static string GitUrlToHttps(string url)
        {
            var pos = url.IndexOf('@');
            if (pos == -1) return url;
            return $"https://{url.Substring(pos + 1).Replace(":[0-9]+", "").Replace(':', '/')}";
        }
    }

    private static Repo? GetRepoInfo(string? directory)
    {
        if (string.IsNullOrEmpty(directory))
            return null;

        return s_cache.GetOrAdd(directory, _ =>
        {
            if (IsGitRoot(directory))
            {
                return GetRepoInfoCore(directory);
            }

            return GetRepoInfo(Path.GetDirectoryName(directory));
        });

        static Repo? GetRepoInfoCore(string directory)
        {
            var remoteUrls = ParseRemoteUrls(directory).ToArray();
            var url = remoteUrls.FirstOrDefault(r => r.key == "origin").value ?? remoteUrls.FirstOrDefault().value;
            if (string.IsNullOrEmpty(url))
                return null;

            var branch = s_branch ?? GetBranchName();
            if (string.IsNullOrEmpty(branch))
                return null;

            return new(directory, url, branch);

            string? GetBranchName()
            {
                var headPath = Path.Combine(directory, ".git", "HEAD");
                var head = File.Exists(headPath) ? File.ReadAllText(headPath).Trim() : null;
                if (head == null)
                    return null;

                if (head.StartsWith("ref: "))
                    return head.Substring("ref: ".Length).Replace("refs/heads/", "").Replace("refs/remotes/", "").Replace("refs/tags/", "");

                return head;
            }
        }

        static bool IsGitRoot(string directory)
        {
            var gitPath = Path.Combine(directory, ".git");
            return Directory.Exists(gitPath);
        }

        static IEnumerable<(string key, string value)> ParseRemoteUrls(string directory)
        {
            var configPath = Path.Combine(directory, ".git", "config");
            if (!File.Exists(configPath))
                yield break;

            var key = "";

            foreach (var text in File.ReadAllLines(configPath))
            {
                var line = text.Trim();
                if (line.StartsWith("["))
                {
                    var remote = Regex.Replace(line, @"\[remote\s+\""(.+)?\""\]", "$1");
                    key = remote != line ? remote : "";
                }
                else if (line.StartsWith("url = ") && !string.IsNullOrEmpty(key))
                {
                    var value = line.Substring("url = ".Length).Trim();
                    yield return (key, value);
                }
            }
        }
    }
}
