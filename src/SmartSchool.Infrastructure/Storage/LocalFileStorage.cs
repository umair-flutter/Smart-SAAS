using Microsoft.Extensions.Options;
using SmartSchool.Application.Common;

namespace SmartSchool.Infrastructure.Storage;

public class StorageSettings
{
    public const string Section = "Storage";

    /// <summary>Folder for uploaded files. Relative paths are resolved against the app's content root.</summary>
    public string RootPath { get; set; } = "App_Data/uploads";
}

/// <summary>Stores files on local disk under {root}/{tenant}/{yyyy}/{guid}{ext}. Swap for blob storage in the cloud.</summary>
public class LocalFileStorage(IOptions<StorageSettings> options) : IFileStorage
{
    private readonly string _root = Path.GetFullPath(options.Value.RootPath);

    public async Task<string> SaveAsync(Guid tenantId, Stream content, string extension, CancellationToken ct)
    {
        var relative = Path.Combine(tenantId.ToString("N"), DateTime.UtcNow.ToString("yyyy"), $"{Guid.NewGuid():N}{extension}");
        var full = Resolve(relative);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        await using var target = File.Create(full);
        await content.CopyToAsync(target, ct);
        return relative.Replace('\\', '/');
    }

    public Task<Stream> OpenReadAsync(string path, CancellationToken ct) =>
        Task.FromResult<Stream>(File.OpenRead(Resolve(path)));

    public Task DeleteAsync(string path, CancellationToken ct)
    {
        var full = Resolve(path);
        if (File.Exists(full)) File.Delete(full);
        return Task.CompletedTask;
    }

    /// <summary>Resolves a stored path and refuses anything that escapes the storage root.</summary>
    private string Resolve(string relative)
    {
        var full = Path.GetFullPath(Path.Combine(_root, relative));
        if (!full.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Invalid storage path.");
        return full;
    }
}
