using Gamma.Erp.Models.Base;
using Gamma.Erp.Models.Documents;
using Gamma.Erp.Models.Users;
using Gamma.Erp.Persistence;
using Gamma.Erp.Services.Entities;
using Microsoft.EntityFrameworkCore;

namespace Gamma.Erp.Services.Documents;

/// <summary>A document waiting for approval.</summary>
public sealed record ApprovalDto(Guid Id, string? Code, string? Type, string? Partner, decimal Total, string? WrittenBy, DateOnly? Date, bool CanApprove);

/// <summary>
/// Approvals (the founder, 2026-11-02): a document of a type that needs them, over the limit of the role of the user who wrote it, can't become the next document until someone
/// whose limit covers it, or an administrator, approves it by hand. Limits are configuration; a company with none approves nothing. Changing a document clears its approval.
/// </summary>
public sealed class Approvals(ErpDbContext db, RequestContext request, Visibility visibility, TimeProvider clock)
{
    /// <summary>What is waiting, oldest first.</summary>
    public async Task<IReadOnlyList<ApprovalDto>> WaitingAsync(string language, CancellationToken ct)
    {
        var companies = await visibility.CompaniesAsync(ct).ConfigureAwait(false);
        var types = await db.Set<DocumentType>().AsNoTracking().Where(t => t.NeedsApproval).ToDictionaryAsync(t => t.Id, t => t.Name.In(language), ct).ConfigureAwait(false);
        var ids = types.Keys.ToList();
        var documents = await db.Set<Document>().AsNoTracking().Where(d => d.Active && ids.Contains(d.TypeId) && d.ApprovedOn == null && companies.Contains(d.CompanyId)).OrderBy(d => d.DocumentDate).ToListAsync(ct).ConfigureAwait(false);
        var writerOf = new Dictionary<Guid, Guid?>();
        foreach (var d in documents)
        {
            writerOf[d.Id] = await WriterAsync(d, ct).ConfigureAwait(false);
        }

        var writers = writerOf.Values.OfType<Guid>().Distinct().ToList();
        var names = await db.Set<User>().AsNoTracking().Where(u => writers.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.Name.In(language) ?? u.Username, ct).ConfigureAwait(false);
        var waiting = new List<ApprovalDto>();
        foreach (var d in documents)
        {
            if (await OverLimitAsync(d, ct).ConfigureAwait(false))
            {
                var total = await TotalAsync(d.Id, ct).ConfigureAwait(false);
                waiting.Add(new ApprovalDto(d.Id, d.Code, types.GetValueOrDefault(d.TypeId), d.FromName ?? d.ToName, total, writerOf[d.Id] is { } w ? names.GetValueOrDefault(w) : null, d.DocumentDate,
                    await MayApproveAsync(d, total, ct).ConfigureAwait(false)));
            }
        }

        return waiting;
    }

    /// <summary>Why a document can't go on: it waits for approval; null when it can.</summary>
    public async Task<string?> RefusalAsync(Document document, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(document);
        var needs = await db.Set<DocumentType>().AsNoTracking().AnyAsync(t => t.Id == document.TypeId && t.NeedsApproval, ct).ConfigureAwait(false);
        return needs && document.ApprovedOn is null && await OverLimitAsync(document, ct).ConfigureAwait(false) ? "it waits for approval." : null;
    }

    /// <summary>Approves a document by hand.</summary>
    public async Task ApproveAsync(Guid id, CancellationToken ct)
    {
        var companies = await visibility.CompaniesAsync(ct).ConfigureAwait(false);
        var document = await db.Set<Document>().FirstOrDefaultAsync(d => d.Id == id && d.Active && companies.Contains(d.CompanyId), ct).ConfigureAwait(false)
            ?? throw ValidationException.For("id", "The document does not exist.");
        if (await RefusalAsync(document, ct).ConfigureAwait(false) is null)
        {
            throw ValidationException.For("id", "This document does not wait for approval.");
        }

        if (!await MayApproveAsync(document, await TotalAsync(id, ct).ConfigureAwait(false), ct).ConfigureAwait(false))
        {
            throw ValidationException.For("rights", $"{ErrorCodes.NotAllowedError}: this document is over your approval limit.");
        }

        (document.ApprovedBy, document.ApprovedOn) = (request.UserId, clock.GetUtcNow());
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    private async Task<bool> OverLimitAsync(Document document, CancellationToken ct)
    {
        var limits = await LimitsAsync(document, ct).ConfigureAwait(false);
        if (limits.Count == 0)
        {
            return false;
        }

        var writer = await WriterAsync(document, ct).ConfigureAwait(false);
        var roles = await db.Set<InterUserCategory>().AsNoTracking().Where(l => l.Active && l.ElementId == writer).Select(l => l.CategoryId).ToListAsync(ct).ConfigureAwait(false);
        var own = limits.Where(l => roles.Contains(l.RoleId)).Select(l => (decimal?)l.Limit).Max() ?? 0;
        return await TotalAsync(document.Id, ct).ConfigureAwait(false) > own;
    }

    private async Task<bool> MayApproveAsync(Document document, decimal total, CancellationToken ct)
    {
        if (request.IsAdministrator)
        {
            return true;
        }

        var limits = await LimitsAsync(document, ct).ConfigureAwait(false);
        return (limits.Where(l => request.RoleIds.Contains(l.RoleId)).Select(l => (decimal?)l.Limit).Max() ?? 0) >= total && limits.Any(l => request.RoleIds.Contains(l.RoleId));
    }

    /// <summary>
    /// Who wrote the document: the user of its first version. Every save writes created_by again, so a person with a higher limit who only saved it does not stand in
    /// for the writer; the row history keeps the first one.
    /// </summary>
    private async Task<Guid?> WriterAsync(Document document, CancellationToken ct)
    {
        var (writer, cursor) = (document.CreatedBy, document.IdChange);
        while (cursor is { } id)
        {
            var copy = await db.Set<Document>().IgnoreQueryFilters().AsNoTracking().Where(d => d.Id == id).Select(d => new { d.CreatedBy, d.IdChange }).FirstOrDefaultAsync(ct).ConfigureAwait(false);
            if (copy is null)
            {
                break;
            }

            (writer, cursor) = (copy.CreatedBy ?? writer, copy.IdChange);
        }

        return writer;
    }

    private Task<List<ApprovalLimit>> LimitsAsync(Document document, CancellationToken ct) =>
        db.Set<ApprovalLimit>().AsNoTracking().Where(l => l.Active && l.OwnerId == document.CompanyId).ToListAsync(ct);

    private async Task<decimal> TotalAsync(Guid id, CancellationToken ct) =>
        await db.Set<DocumentTotal>().AsNoTracking().Where(t => t.Active && t.DocumentId == id).SumAsync(t => t.ValueVatLcu ?? 0, ct).ConfigureAwait(false);
}
