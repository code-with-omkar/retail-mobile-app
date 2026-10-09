using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Xunit;

namespace QuickCommerce.Tests;

/// <summary>
/// Guard against committing a Google API key (docs/plans/P4-addresses-and-serviceability.md, "Key and setup"). Google keys start with
/// "AIza" followed by 35 characters. The key belongs in an untracked local file (mobile/android/secrets.properties and a local
/// --dart-define-from-file), never in source, config or documents. The test reports where, never the value.
/// </summary>
public sealed class ApiKeyGuardTests
{
    // Built from parts so this file does not contain something that looks like a key.
    private static readonly Regex GoogleKey = new("AI" + "za[0-9A-Za-z_\\-]{35}", RegexOptions.Compiled);

    private static readonly string[] Extensions = [".cs", ".json", ".config", ".xml", ".yml", ".yaml", ".ps1", ".sh", ".env", ".gradle", ".kts", ".properties", ".dart", ".md", ".html", ".sql", ".txt", ".plist", ".ts"];

    // Local files that legitimately hold keys and are ignored by git (see the .gitignore test below).
    private static readonly string[] LocalSecretFiles = ["secrets.properties", "local.properties", "local.json"];

    private static readonly string[] SkippedFolders = ["bin", "obj", "node_modules", "dist", "build", ".git", ".vs", ".dart_tool", ".gradle", ".idea", "artifacts", ".angular"];

    [Fact]
    public void No_google_api_key_is_in_source_config_or_documents()
    {
        var root = FindRepositoryRoot();
        var offenders = new List<string>();

        foreach (var folder in new[] { "src", "deploy", "mobile", "docs", "admin-portal" })
        {
            var path = Path.Combine(root, folder);
            if (!Directory.Exists(path))
            {
                continue;
            }

            foreach (var file in EnumerateFiles(path))
            {
                var lineNumber = 0;
                foreach (var line in File.ReadLines(file))
                {
                    lineNumber++;
                    if (GoogleKey.IsMatch(line))
                    {
                        offenders.Add($"{Path.GetRelativePath(root, file)}:{lineNumber}");
                    }
                }
            }
        }

        Assert.True(offenders.Count == 0, "A Google API key was found in: " + string.Join(", ", offenders));
    }

    [Theory]
    [InlineData("AI" + "zaSyA-1234567890abcdefghijklmnopqrstuv", true)]
    [InlineData("<meta-data android:name=\"com.google.android.geo.API_KEY\" android:value=\"AI" + "zaSyD_abcdefghijklmnopqrstuvwxyz012345\"/>", true)]
    [InlineData("android:value=\"${MAPS_API_KEY}\"", false)]
    [InlineData("MAPS_API_KEY=", false)]
    [InlineData("AI" + "za-too-short", false)]
    [InlineData("a sentence about AI and zaps", false)]
    public void The_detection_pattern_flags_google_keys_only(string line, bool expected) => Assert.Equal(expected, GoogleKey.IsMatch(line));

    [Fact]
    public void The_local_secret_files_are_ignored_by_git()
    {
        var gitignore = File.ReadAllLines(Path.Combine(FindRepositoryRoot(), ".gitignore")).Select(line => line.Trim()).ToArray();
        var mobileIgnore = Path.Combine(FindRepositoryRoot(), "mobile", ".gitignore");
        var all = gitignore.Concat(File.Exists(mobileIgnore) ? File.ReadAllLines(mobileIgnore).Select(line => line.Trim()) : []).ToArray();

        foreach (var name in new[] { "secrets.properties", "local.json" })
        {
            Assert.True(all.Any(line => line.EndsWith(name, StringComparison.Ordinal) && !line.StartsWith('#')), $"{name} must be listed in a .gitignore so a key kept there is never committed.");
        }
    }

    private static IEnumerable<string> EnumerateFiles(string directory)
    {
        foreach (var file in Directory.EnumerateFiles(directory))
        {
            if (Extensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase)
                && !LocalSecretFiles.Contains(Path.GetFileName(file), StringComparer.OrdinalIgnoreCase)
                && !file.EndsWith("ApiKeyGuardTests.cs", StringComparison.OrdinalIgnoreCase))
            {
                yield return file;
            }
        }

        foreach (var sub in Directory.EnumerateDirectories(directory))
        {
            if (!SkippedFolders.Contains(Path.GetFileName(sub), StringComparer.OrdinalIgnoreCase))
            {
                foreach (var file in EnumerateFiles(sub))
                {
                    yield return file;
                }
            }
        }
    }

    private static string FindRepositoryRoot([CallerFilePath] string sourceFile = "")
    {
        var directory = new DirectoryInfo(Path.GetDirectoryName(sourceFile)!);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "QuickCommerce.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Could not find QuickCommerce.slnx above the test source folder.");
    }
}
