using System.Security.Cryptography;
using Gamma.Erp.Models.Base.Dto;
using Gamma.Erp.Models.Platform;
using Gamma.Erp.Persistence;
using Gamma.Erp.Services.Entities;
using Microsoft.EntityFrameworkCore;

namespace Gamma.Erp.Services.Files;

/// <summary>What the API gives back about a stored file.</summary>
public sealed record StoredFileDto(Guid Id, string FileName, string ContentType, long Size, string Sha256, string Link, DateTimeOffset CreatedOn);

/// <summary>
/// Files: kept for the request's node (so its node and those above it see it, as every row), checked for size and content type, their bytes
/// in the store under a key of the tenant and the file's id. A file is read back only when its row is visible and its bytes still match.
/// </summary>
public sealed class FileService(ErpDbContext db, RequestContext request, Visibility visibility, IFileStore store, FileOptions options)
{
    public async Task<StoredFileDto> UploadAsync(string fileName, string? contentType, Stream content, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(content);
        var name = Path.GetFileName((fileName ?? "").Replace('\\', '/')).Trim();
        if (name.Length is 0 or > 255)
        {
            throw ValidationException.For("file", $"{ErrorCodes.FileError}: the file needs a name of at most 255 characters.");
        }

        var type = string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType.Split(';')[0].Trim().ToLowerInvariant();
        if (!options.ContentTypes.Contains(type))
        {
            throw ValidationException.For("file", $"{ErrorCodes.FileError}: files of type {type} are not accepted.");
        }

        var content_ = await ReadWithinAsync(content, ct).ConfigureAwait(false);
        var row = new StoredFile
        {
            FileName = name, ContentType = type, Size = content_.Length, Sha256 = Convert.ToHexStringLower(SHA256.HashData(content_)),
            TenantId = request.TenantId, OwnerId = request.NodeId, CreatedBy = request.UserId,
        };
        row.StorageKey = $"{request.TenantId:N}/{row.Id:N}";
        await store.PutAsync(row.StorageKey, new MemoryStream(content_, writable: false), ct).ConfigureAwait(false);
        db.Add(row);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return ToDto(row);
    }

    /// <summary>The file's row, when the request sees it (its tenant, a node at or below the request's), else null.</summary>
    /// <summary>The files the request sees (B10, C3 phase 7), newest first, matching a name; a page at a time.</summary>
    public async Task<PageDto<StoredFileDto>> ListAsync(string? search, int page, int pageSize, CancellationToken ct)
    {
        (page, pageSize) = (Math.Max(page, 1), Math.Clamp(pageSize, 1, 500));
        var nodes = await visibility.NodesAsync(ct).ConfigureAwait(false);
        // The name contains the search, case aside; % and _ typed are taken literally.
        var pattern = string.IsNullOrWhiteSpace(search) ? null
            : "%" + search.Trim().Replace("\\", "\\\\", StringComparison.Ordinal).Replace("%", "\\%", StringComparison.Ordinal).Replace("_", "\\_", StringComparison.Ordinal) + "%";
        var query = db.Set<StoredFile>().AsNoTracking().Where(f => f.Active && f.OwnerId != null && nodes.Contains(f.OwnerId.Value)
            && (pattern == null || EF.Functions.ILike(f.FileName, pattern)));
        var total = await query.CountAsync(ct).ConfigureAwait(false);
        var rows = await query.OrderByDescending(f => f.CreatedOn).ThenByDescending(f => f.Id).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct).ConfigureAwait(false);
        return new PageDto<StoredFileDto>([.. rows.Select(ToDto)], total, page, pageSize);
    }

    public async Task<StoredFileDto?> FileAsync(Guid id, CancellationToken ct) => await VisibleAsync(id, ct).ConfigureAwait(false) is { } row ? ToDto(row) : null;

    /// <summary>The file's bytes, checked against the hash kept when it was stored; null when the request does not see it.</summary>
    public async Task<(StoredFileDto File, byte[] Content)?> ReadAsync(Guid id, CancellationToken ct)
    {
        if (await VisibleAsync(id, ct).ConfigureAwait(false) is not { } row)
        {
            return null;
        }

        await using var stream = await store.OpenAsync(row.StorageKey, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"The store has lost file {row.Id}.");
        using var bytes = new MemoryStream();
        await stream.CopyToAsync(bytes, ct).ConfigureAwait(false);
        var content = bytes.ToArray();
        return Convert.ToHexStringLower(SHA256.HashData(content)) == row.Sha256 ? (ToDto(row), content)
            : throw new InvalidOperationException($"File {row.Id} no longer matches what was stored.");
    }

    // Read whole, within the limit, before anything is stored: nothing is kept unless all of it arrived and fits.
    private async Task<byte[]> ReadWithinAsync(Stream content, CancellationToken ct)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        for (int read; (read = await content.ReadAsync(chunk, ct).ConfigureAwait(false)) > 0;)
        {
            if (buffer.Length + read > options.MaxBytes)
            {
                throw ValidationException.For("file", $"{ErrorCodes.FileError}: the file is larger than {options.MaxBytes} bytes.");
            }

            buffer.Write(chunk, 0, read);
        }

        return buffer.Length > 0 ? buffer.ToArray() : throw ValidationException.For("file", $"{ErrorCodes.FileError}: the file is empty.");
    }

    /// <summary>Takes a file away by hand (switched off, never deleted); refused while a property value still points at it.</summary>
    public async Task RemoveAsync(Guid id, CancellationToken ct)
    {
        _ = await VisibleAsync(id, ct).ConfigureAwait(false) ?? throw ValidationException.For("id", "The file does not exist.");
        var pattern = $"%{id}%";
        foreach (var info in db.Model.GetEntityTypes().Where(e => typeof(Gamma.Erp.Models.Base.ElementInfo).IsAssignableFrom(e.ClrType) && !e.ClrType.IsAbstract))
        {
            var sql = $"SELECT EXISTS (SELECT 1 FROM \"{info.GetSchema()}\".\"{info.GetTableName()}\" WHERE active AND value::text LIKE {{0}}) AS \"Value\"";
            if (await db.Database.SqlQueryRaw<bool>(sql, pattern).SingleAsync(ct).ConfigureAwait(false))
            {
                throw ValidationException.For("id", "A property still uses this file: take it off there first.");
            }
        }

        var row = await db.Set<StoredFile>().FirstAsync(f => f.Id == id, ct).ConfigureAwait(false);
        row.Active = false;
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    private async Task<StoredFile?> VisibleAsync(Guid id, CancellationToken ct)
    {
        var nodes = await visibility.NodesAsync(ct).ConfigureAwait(false);
        return await db.Set<StoredFile>().AsNoTracking().FirstOrDefaultAsync(f => f.Id == id && f.Active && f.OwnerId != null && nodes.Contains(f.OwnerId.Value), ct)
            .ConfigureAwait(false);
    }

    private static StoredFileDto ToDto(StoredFile f) => new(f.Id, f.FileName, f.ContentType, f.Size, f.Sha256, $"/api/v1/files/{f.Id}", f.CreatedOn);
}
