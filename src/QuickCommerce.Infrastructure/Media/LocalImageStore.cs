using System.Text.RegularExpressions;
using QuickCommerce.Application.Interfaces;

namespace QuickCommerce.Infrastructure.Media;

/// <summary>Product photos in a folder on the API machine, served by the API under /media. Moving to cloud storage later means a new IImageStore, nothing else.</summary>
public sealed partial class LocalImageStore(string rootPath) : IImageStore
{
    public const string RequestPath = "/media";
    private const string Folder = "products";
    private static readonly string[] Folders = [Folder, "offers"];

    [GeneratedRegex("^[a-z0-9-]{1,80}$")]
    private static partial Regex SafeName();

    [GeneratedRegex("^/media/(products|offers)/([a-z0-9-]{1,80})\\.(jpg|png|webp)$")]
    private static partial Regex OwnPath();

    public string RootPath { get; } = Path.GetFullPath(rootPath);

    public Task<string> SaveAsync(string name, string extension, byte[] content, CancellationToken cancellationToken = default) =>
        SaveAsync(Folder, name, extension, content, cancellationToken);

    public async Task<string> SaveAsync(string folder, string name, string extension, byte[] content, CancellationToken cancellationToken = default)
    {
        if (!Folders.Contains(folder) || !SafeName().IsMatch(name) || extension is not ("jpg" or "png" or "webp"))
        {
            throw new ArgumentException("Unsafe picture name.");
        }

        var directory = Path.Combine(RootPath, folder);
        Directory.CreateDirectory(directory);
        await File.WriteAllBytesAsync(Path.Combine(directory, $"{name}.{extension}"), content, cancellationToken);
        return $"{RequestPath}/{folder}/{name}.{extension}";
    }

    public Task DeleteAsync(string? publicPath, CancellationToken cancellationToken = default)
    {
        var match = publicPath is null ? null : OwnPath().Match(publicPath);
        if (match is { Success: true })
        {
            var file = Path.Combine(RootPath, match.Groups[1].Value, $"{match.Groups[2].Value}.{match.Groups[3].Value}");
            if (File.Exists(file))
            {
                File.Delete(file);
            }
        }

        return Task.CompletedTask;
    }
}
