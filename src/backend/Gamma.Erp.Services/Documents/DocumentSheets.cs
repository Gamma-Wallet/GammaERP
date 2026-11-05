using Gamma.Erp.Models.Depreciation;
using Gamma.Erp.Models.Documents;
using Gamma.Erp.Models.Documents.Dto;
using Microsoft.EntityFrameworkCore;

namespace Gamma.Erp.Services.Documents;

/// <summary>A sheet with its journals: what the sheet's one grid saves.</summary>
public sealed record SheetDto(DocumentDto Sheet, IReadOnlyList<DocumentDto> Journals);

/// <summary>
/// Sheets: a sheet is a parent document with no transactions of its own; it holds journals, documents under it of a type
/// under its type, each with one transaction, one row of the sheet's grid. A row may be debit only or credit only, so debit = credit holds
/// over the whole sheet, not over each journal: posting a sheet posts its journals with it, all or nothing.
/// </summary>
public sealed partial class DocumentService
{
    /// <summary>The journals under a sheet, in their order.</summary>
    public async Task<IReadOnlyList<DocumentDto>> JournalsAsync(Guid sheet, CancellationToken ct)
    {
        var ids = await db.Set<Document>().AsNoTracking().Where(d => d.IdSup == sheet).OrderBy(d => d.Id).Select(d => d.Id).ToListAsync(ct).ConfigureAwait(false);
        var journals = new List<DocumentDto>();
        foreach (var id in ids)
        {
            if (await DocumentAsync(id, ct).ConfigureAwait(false) is { } journal)
            {
                journals.Add(journal);
            }
        }

        return journals;
    }

    /// <summary>Saves a sheet and its rows together: each row one journal under it, rows taken out switched off. All or nothing.</summary>
    public async Task<SheetDto> SaveSheetAsync(SheetDto dto, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(dto);
        var types = dto.Journals.Select(j => j.TypeId).Distinct().ToList();
        var under = await db.Set<DocumentType>().AsNoTracking().Where(t => types.Contains(t.Id) && t.IdSup == dto.Sheet.TypeId).Select(t => t.Id).ToListAsync(ct).ConfigureAwait(false);
        var errors = new Dictionary<string, string>();
        foreach (var (journal, i) in dto.Journals.Select((j, i) => (j, i)))
        {
            if (!under.Contains(journal.TypeId))
            {
                errors[$"journals[{i}].typeId"] = $"{ErrorCodes.NotConfiguredError}: this document type is not one of the sheet's.";
            }

            if (journal.Transactions.Count != 1)
            {
                errors[$"journals[{i}].transactions"] = "A journal has exactly one transaction.";
            }
        }

        if (errors.Count > 0)
        {
            throw new ValidationException(errors);
        }

        await using var transaction = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false) : null;
        var sheet = await SaveAsync(dto.Sheet with { Transactions = [] }, ct).ConfigureAwait(false);
        var kept = dto.Journals.Where(j => j.Id is not null).Select(j => j.Id!.Value).ToHashSet();
        foreach (var gone in (await db.Set<Document>().AsNoTracking().Where(d => d.IdSup == sheet.Id).Select(d => d.Id).ToListAsync(ct).ConfigureAwait(false)).Where(d => !kept.Contains(d)))
        {
            await DeleteAsync(gone, ct).ConfigureAwait(false);
        }

        foreach (var (journal, i) in dto.Journals.Select((j, i) => (j, i)))
        {
            var categories = await db.Set<InterDocumentTypeCategory>().AsNoTracking().Where(l => l.TypeId == journal.TypeId).Select(l => l.CategoryId).ToListAsync(ct).ConfigureAwait(false);
            try
            {
                await SaveAsync(journal with { IdSup = sheet.Id, CategoryIds = categories, PostingDate = journal.PostingDate ?? sheet.PostingDate, DocumentDate = sheet.DocumentDate }, ct)
                    .ConfigureAwait(false);
            }
            catch (ValidationException refused)
            {
                throw new ValidationException(refused.Errors.ToDictionary(e => $"journals[{i}].{e.Key}", e => e.Value));
            }
        }

        if (transaction is not null)
        {
            await transaction.CommitAsync(ct).ConfigureAwait(false);
        }

        return new SheetDto((await DocumentAsync(sheet.Id!.Value, ct).ConfigureAwait(false))!, await JournalsAsync(sheet.Id!.Value, ct).ConfigureAwait(false));
    }

    /// <summary>
    /// Posts a document. A sheet posts with its journals not yet posted, all or nothing, and debit = credit is checked over them together; a
    /// sheet of a type that has journals is refused without any. Posted is final.
    /// </summary>
    public async Task<DocumentDto> PostAsync(Guid id, CancellationToken ct)
    {
        var children = await db.Set<Document>().AsNoTracking().Where(d => d.IdSup == id).Select(d => new { d.Id, d.Posted }).ToListAsync(ct).ConfigureAwait(false);
        var journals = children.Where(c => !c.Posted).OrderBy(c => c.Id).Select(c => c.Id).ToList();
        if (journals.Count == 0)
        {
            if (children.Count == 0 && await IsEmptySheetAsync(id, ct).ConfigureAwait(false))
            {
                throw ValidationException.For("journals", "The sheet has no rows to post.");
            }

            return await PostOneAsync(id, ct).ConfigureAwait(false);
        }

        await using var transaction = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false) : null;
        _sheetTotals = (0, 0);
        try
        {
            foreach (var journal in journals)
            {
                await PostOneAsync(journal, ct).ConfigureAwait(false);
            }

            if (_sheetTotals is var (debit, credit) && debit != credit)
            {
                throw ValidationException.For("entries", $"{ErrorCodes.UnbalancedError}: debit {debit:0.00} ≠ credit {credit:0.00}.");
            }
        }
        finally
        {
            _sheetTotals = null;
        }

        var posted = await PostOneAsync(id, ct).ConfigureAwait(false);
        if (transaction is not null)
        {
            await transaction.CommitAsync(ct).ConfigureAwait(false);
        }

        return posted;
    }

    /// <summary>
    /// Reverses a posted sheet: a sheet of its reversal type holding the reversal of each of its journals (debit and credit swapped), posted
    /// together, all or nothing. Only a whole sheet is reversed.
    /// </summary>
    private async Task<DocumentDto> ReverseSheetAsync(Guid id, DateOnly postingDate, CancellationToken ct)
    {
        var original = await WritableAsync(id, ct).ConfigureAwait(false);
        if (!original.Posted)
        {
            throw ValidationException.For("posted", $"{ErrorCodes.ReversalError}: only a posted document is reversed; an unposted one is changed.");
        }

        if (await db.Set<DocumentType>().AsNoTracking().Where(t => t.ReversedDocumentTypeId == original.TypeId && t.Active && t.IsReversal).Select(t => (Guid?)t.Id)
                .FirstOrDefaultAsync(ct).ConfigureAwait(false) is not { } reversalType)
        {
            throw ValidationException.For("typeId", $"{ErrorCodes.NotConfiguredError}: no reversal type reverses this document type; set one first.");
        }

        var journals = await db.Set<Document>().AsNoTracking().Where(d => d.IdSup == id).OrderBy(d => d.Id).Select(d => d.Id).ToListAsync(ct).ConfigureAwait(false);
        var categories = await db.Set<InterDocumentTypeCategory>().AsNoTracking().Where(l => l.TypeId == reversalType).Select(l => l.CategoryId).ToListAsync(ct).ConfigureAwait(false);
        await using var transaction = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false) : null;
        DocumentDto reversal;
        _reversing = original;
        try
        {
            reversal = await SaveAsync(new DocumentDto { TypeId = reversalType, CategoryIds = categories, DocumentDate = postingDate, PostingDate = postingDate, CreatedFromIds = [id] }, ct)
                .ConfigureAwait(false);
        }
        finally
        {
            _reversing = null;
        }

        _reversalUnder = reversal.Id;
        try
        {
            foreach (var journal in journals)
            {
                await ReverseAsync(journal, null, postingDate, ct).ConfigureAwait(false);
            }
        }
        finally
        {
            _reversalUnder = null;
        }

        var posted = await PostAsync(reversal.Id!.Value, ct).ConfigureAwait(false);
        if (transaction is not null)
        {
            await transaction.CommitAsync(ct).ConfigureAwait(false);
        }

        return posted;
    }

    /// <summary>The depreciation rows of a journal, or of the journals under a sheet.</summary>
    private async Task<List<AssetDepreciation>> RunRowsAsync(Guid id, CancellationToken ct)
    {
        var journals = db.Set<Document>().Where(d => d.Id == id || d.IdSup == id).Select(d => (Guid?)d.Id);
        return await db.Set<AssetDepreciation>().Where(r => journals.Contains(r.DocumentId)).ToListAsync(ct).ConfigureAwait(false);
    }

    /// <summary>A depreciation journal is reversed only when no later run was built on its book values (the founder, 2026-11-01).</summary>
    private async Task CheckLatestRunAsync(Guid id, CancellationToken ct)
    {
        var rows = await RunRowsAsync(id, ct).ConfigureAwait(false);
        if (rows.Count == 0)
        {
            return;
        }

        var (values, last) = (rows.Select(r => r.AssetValueId).ToList(), rows.Max(r => r.DepreciationDate));
        if (await db.Set<AssetDepreciation>().AsNoTracking().AnyAsync(r => values.Contains(r.AssetValueId) && r.DepreciationDate > last, ct).ConfigureAwait(false))
        {
            throw ValidationException.For("document", $"{ErrorCodes.ReversalError}: a later depreciation run stands on this one; undo the later run first.");
        }
    }

    /// <summary>Reversing a depreciation journal cancels its run: the assets go back to their book value before it.</summary>
    private async Task CancelRunAsync(Guid id, CancellationToken ct)
    {
        var rows = await RunRowsAsync(id, ct).ConfigureAwait(false);
        rows.ForEach(r => r.Active = false);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    /// <summary>A document with no lines of a type that has types under it: a sheet with nothing on it.</summary>
    private async Task<bool> IsEmptySheetAsync(Guid id, CancellationToken ct)
    {
        var type = await db.Set<Document>().AsNoTracking().Where(d => d.Id == id).Select(d => (Guid?)d.TypeId).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        return type is not null && await db.Set<DocumentType>().AsNoTracking().AnyAsync(t => t.IdSup == type, ct).ConfigureAwait(false)
            && !await db.Set<Models.Transactions.Transaction>().AsNoTracking().AnyAsync(t => t.DocumentId == id, ct).ConfigureAwait(false);
    }
}
