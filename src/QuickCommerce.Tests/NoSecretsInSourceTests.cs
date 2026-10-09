using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Xunit;

namespace QuickCommerce.Tests;

/// <summary>
/// Guard against committing credentials (docs/copilot-instructions.md section 22). Fails when a connection string with an
/// inline password or SQL login, or a JSON "Password" property with a value, is found in source or deployment files.
/// Use Windows authentication, user secrets, or environment variables such as ConnectionStrings__QuickCommerceDb instead.
/// </summary>
public sealed class NoSecretsInSourceTests
{
    // A non-empty Password=/Pwd= value: not ==, =>, a member access such as settings.Password=, or an empty value.
    private static readonly Regex InlinePassword = new(@"(?<![\w.])(Password|Pwd)\s*=(?![=>])\s*(?![;""'\s])[^;""'\s]+", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Treated as a connection string only when the same line also names a server or database.
    private static readonly Regex ConnectionContext = new(@"\b(Server|Data Source|Host|Initial Catalog|Database)\s*=", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex SqlLogin = new(@"\bUser\s*Id\s*=\s*sa\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // A JSON property named exactly "Password" (or "Pwd") with a non-empty string value.
    private static readonly Regex JsonPassword = new(@"""(Password|Pwd)""\s*:\s*""[^""]+""", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly string[] ScannedExtensions = [".cs", ".json", ".config", ".xml", ".yml", ".yaml", ".ps1", ".sh", ".env"];
    private static readonly string[] SkippedFolders = ["bin", "obj", "node_modules", "dist", "build", ".git", ".vs"];

    private static bool LooksLikeACredential(string line) =>
        (InlinePassword.IsMatch(line) && ConnectionContext.IsMatch(line)) || SqlLogin.IsMatch(line) || JsonPassword.IsMatch(line);

    [Fact]
    public void No_connection_string_in_source_or_deploy_files_contains_an_inline_password_or_sa_login()
    {
        var root = FindRepositoryRoot(ThisSourceFile());
        var offenders = new List<string>();

        foreach (var folder in new[] { "src", "deploy" })
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
                    if (LooksLikeACredential(line))
                    {
                        // Report where, never the value.
                        offenders.Add($"{Path.GetRelativePath(root, file)}:{lineNumber}");
                    }
                }
            }
        }

        Assert.True(offenders.Count == 0, "Credentials found in: " + string.Join(", ", offenders));
    }

    [Theory]
    [InlineData("Server=x;Database=y;User Id=sa;Password=Secret1;", true)]
    [InlineData("Server=x;Database=y;Pwd=abc123;", true)]
    [InlineData("Data Source=x;Password = Secret1;", true)]
    [InlineData("User Id=sa;Server=x;", true)]
    [InlineData("\"Password\": \"hunter2hunter2\"", true)]
    [InlineData("Server=x;Database=y;Trusted_Connection=True;", false)]
    [InlineData("Server=x;Password=;", false)]
    [InlineData("\"Password\": \"\"", false)]
    [InlineData("\"DevelopmentAdminPassword\": \"value\"", false)]
    [InlineData("else if (password == context.InstanceToValidate.CurrentPassword)", false)]
    [InlineData("var hash = hasher.HashPassword(user, password);", false)]
    [InlineData("settings.Password = configuration[\"x\"];", false)]
    [InlineData("public string Password { get; set; } = \"\";", false)]
    public void The_detection_pattern_flags_inline_credentials_only(string line, bool expected) =>
        Assert.Equal(expected, LooksLikeACredential(line));

    private static string ThisSourceFile([CallerFilePath] string path = "") => path;

    private static IEnumerable<string> EnumerateFiles(string directory)
    {
        foreach (var file in Directory.EnumerateFiles(directory))
        {
            if (ScannedExtensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase) && !file.EndsWith("NoSecretsInSourceTests.cs", StringComparison.OrdinalIgnoreCase))
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

    // Starts from this source file, so it works wherever the build output goes (for example a custom artifacts path in CI).
    private static string FindRepositoryRoot(string sourceFile)
    {
        var dir = new DirectoryInfo(string.IsNullOrEmpty(sourceFile) ? AppContext.BaseDirectory : Path.GetDirectoryName(sourceFile)!);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "QuickCommerce.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("Could not find QuickCommerce.slnx above the test source folder.");
    }
}
