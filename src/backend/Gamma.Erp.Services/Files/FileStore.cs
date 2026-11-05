namespace Gamma.Erp.Services.Files;

/// <summary>Where the files' bytes are kept: chosen at start-up (Files:Store), the same for every tenant; the rows say whose a file is.</summary>
public interface IFileStore
{
    Task PutAsync(string key, Stream content, CancellationToken ct);

    /// <summary>The file's bytes, or null when the store has no such key.</summary>
    Task<Stream?> OpenAsync(string key, CancellationToken ct);
}

/// <summary>The files in a folder of the host (in the container, a volume): one file per key, written whole or not at all.</summary>
public sealed class DiskFileStore : IFileStore
{
    private readonly string _root;

    public DiskFileStore(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        _root = Path.GetFullPath(root);
        Directory.CreateDirectory(_root);
    }

    public async Task PutAsync(string key, Stream content, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(content);
        var path = PathOf(key);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + ".part";
        await using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true))
        {
            await content.CopyToAsync(file, ct).ConfigureAwait(false);
        }

        File.Move(temporary, path);
    }

    public Task<Stream?> OpenAsync(string key, CancellationToken ct)
    {
        var path = PathOf(key);
        return Task.FromResult<Stream?>(File.Exists(path) ? new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true) : null);
    }

    // The keys are Gamma's own (tenant/id), but the path is still checked to stay inside the root.
    private string PathOf(string key)
    {
        var path = Path.GetFullPath(Path.Combine(_root, key));
        return path.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.Ordinal) ? path : throw new ArgumentException("The key leaves the store.", nameof(key));
    }
}

/// <summary>The file settings of the host (Files:…): which store, its folder, the largest file and the content types accepted.</summary>
public sealed record FileOptions(string Store, string? Root, long MaxBytes, IReadOnlySet<string> ContentTypes)
{
    public static readonly IReadOnlySet<string> DefaultContentTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "application/pdf", "image/png", "image/jpeg", "image/webp", "image/gif", "text/plain", "text/csv", "application/xml", "text/xml",
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
    };
}
