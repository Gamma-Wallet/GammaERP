using System.ComponentModel;
using Gamma.Erp.Persistence;
using Gamma.Erp.Services;
using Gamma.Erp.Services.Documents;
using Gamma.Erp.Services.Entities;
using Gamma.Erp.Services.Files;
using Gamma.Erp.Services.Migration;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace Gamma.Erp.Api.Mcp.Tools;

/// <summary>
/// Files through Claude, as the screens upload and download them: the file store, a bank statement's import, and the Migration menu's
/// templates, exports and imports. A file's content goes both ways as base64; the services keep their own size limits and checks.
/// </summary>
[McpServerToolType]
public sealed class DataTools(ErpDbContext db, RequestContext request, Visibility visibility, FileService files, BankStatements statements, MigrationService migration)
{
    [McpServerTool(Name = "files", Title = "The file store", ReadOnly = false, Destructive = false)]
    [Description("The Files screen: action list (files by name, a page at a time), info (one file), read (its content as base64), upload (a new file from base64, with its name and type; answers with its link, /api/v1/files/{id}, for a File or Photo property), or remove (switches a file off). Needs the Files screen. Upload and remove need an explicit yes; then confirm=true.")]
    public Task<object> FilesAsync(
        [Description("list, info, read, upload or remove.")] string action,
        [Description("info, read, remove: the file's id.")] Guid? id = null,
        [Description("list: optional, part of the name.")] string? search = null,
        [Description("upload: the file's name, e.g. contract.pdf.")] string? fileName = null,
        [Description("upload: its content type, e.g. application/pdf or image/png.")] string? contentType = null,
        [Description("upload: the content, base64.")] string? base64 = null,
        [Description("list: page number, from 1.")] int? page = null,
        [Description("True only after the user said yes (upload, remove).")] bool confirm = false, CancellationToken ct = default) =>
        Erp.Run<object>(async () =>
        {
            Erp.NeedsPlace(request);
            await Registers.Screen(visibility, "files", ct).ConfigureAwait(false);
            switch (action.Trim().ToUpperInvariant())
            {
                case "LIST":
                    return await files.ListAsync(search, Math.Max(page ?? 1, 1), Registers.DefaultPage, ct).ConfigureAwait(false);
                case "INFO":
                    return (object?)await files.FileAsync(id ?? throw new McpException("Give the file."), ct).ConfigureAwait(false) ?? throw new McpException("There is no such file that you may see.");
                case "READ":
                    var read = await files.ReadAsync(id ?? throw new McpException("Give the file."), ct).ConfigureAwait(false) ?? throw new McpException("There is no such file that you may see.");
                    return new { read.File.FileName, read.File.ContentType, base64 = Convert.ToBase64String(read.Content) };
                case "UPLOAD":
                    Erp.NeedsYes(confirm, "the file to upload");
                    await using (var content = new MemoryStream(Bytes(base64)))
                    {
                        return await files.UploadAsync(fileName ?? throw new McpException("Give the file's name."), contentType, content, ct).ConfigureAwait(false);
                    }

                case "REMOVE":
                    Erp.NeedsYes(confirm, "the file to switch off");
                    await files.RemoveAsync(id ?? throw new McpException("Give the file."), ct).ConfigureAwait(false);
                    return new { removed = id };
                default:
                    throw new McpException("The action is list, info, read, upload or remove.");
            }
        });

    [McpServerTool(Name = "import_bank_statement", Title = "Import a bank statement", ReadOnly = false, Destructive = false)]
    [Description("Imports a bank statement file (base64) for a bank account in one of the company's statement formats, as the Bank statements screen does: its lines come in undecided, then are matched, ignored or cleared with decide_statement_line. Lines already imported are skipped. bank_statements lists the banks and formats with what=banks or formats. Needs the Bank statements screen. Show the user the bank, format and file and get an explicit yes; then confirm=true.")]
    public Task<object> ImportBankStatementAsync(
        [Description("True only after the user said yes.")] bool confirm,
        [Description("The bank account (cost centre id or exact code).")] string bank,
        [Description("The statement format: its id (from the formats).")] Guid format,
        [Description("The file's name, e.g. statement-2026-10.csv.")] string fileName,
        [Description("The file's content, base64.")] string base64, CancellationToken ct = default) =>
        Erp.Run<object>(async () =>
        {
            Erp.NeedsYes(confirm, "the bank, format and statement file to import");
            Erp.NeedsPlace(request);
            await Registers.Screen(visibility, "bank-statements", ct).ConfigureAwait(false);
            var bankId = await Registers.ElementAsync(s => Registers.CostCenters(db, request, visibility).LookupAsync(null, null, s, Registers.English, 1, Registers.MaxPage, ct), bank, "bank")
                .ConfigureAwait(false);
            var bytes = Bytes(base64);
            if (bytes.Length > BankStatements.MaxBytes)
            {
                throw new McpException("The file is too large.");
            }

            await using var content = new MemoryStream(bytes);
            return await statements.ImportAsync(content, fileName, bankId, format, ct).ConfigureAwait(false);
        });

    [McpServerTool(Name = "bank_statement_options", Title = "Banks and statement formats", ReadOnly = true)]
    [Description("The bank accounts that take statements and the company's statement formats, for import_bank_statement. Needs the Bank statements screen.")]
    public Task<object> BankStatementOptionsAsync(CancellationToken ct = default) =>
        Erp.Run<object>(async () =>
        {
            Erp.NeedsPlace(request);
            await Registers.Screen(visibility, "bank-statements", ct).ConfigureAwait(false);
            return new
            {
                banks = await statements.BanksAsync(Registers.English, ct).ConfigureAwait(false),
                formats = await statements.FormatsAsync(Registers.English, ct).ConfigureAwait(false),
            };
        });

    [McpServerTool(Name = "migration", Title = "Bring data in (Migration)", ReadOnly = false, Destructive = false)]
    [Description("The Migration menu: action steps (each step in order: what it brings in, what it waits for, its rows and last run), opening (the account the opening steps balance on and where it stands), template (a step's empty Excel template, base64), export (a step's rows now, as Excel, base64), or import (an Excel file for a step, base64: without confirm it only checks and lists every error; with confirm=true, after the user's yes, it saves all the rows or none).")]
    public Task<object> MigrationAsync(
        [Description("steps, opening, template, export or import.")] string action,
        [Description("template, export, import: the step's code (from steps).")] string? step = null,
        [Description("import: the file's name.")] string? fileName = null,
        [Description("import: the Excel file, base64.")] string? base64 = null,
        [Description("import: true saves (after the user's yes); false only checks.")] bool confirm = false, CancellationToken ct = default) =>
        Erp.Run<object>(async () =>
        {
            Erp.NeedsPlace(request);
            switch (action.Trim().ToUpperInvariant())
            {
                case "STEPS":
                    return await migration.StepsAsync(Registers.English, ct).ConfigureAwait(false);
                case "OPENING":
                    return (object?)await migration.OpeningAsync(Registers.English, ct).ConfigureAwait(false) ?? new { opening = (object?)null };
                case "TEMPLATE":
                    var template = await migration.TemplateAsync(step ?? throw new McpException("Give the step."), ct).ConfigureAwait(false);
                    return new { fileName = $"{step}-template.xlsx", contentType = MigrationService.ContentType, base64 = Convert.ToBase64String(template) };
                case "EXPORT":
                    var export = await migration.ExportAsync(step ?? throw new McpException("Give the step."), ct).ConfigureAwait(false);
                    return new { fileName = $"{step}.xlsx", contentType = MigrationService.ContentType, base64 = Convert.ToBase64String(export) };
                case "IMPORT":
                    var bytes = Bytes(base64);
                    if (bytes.Length > MigrationService.MaxBytes)
                    {
                        throw new McpException("The file is too large.");
                    }

                    await using (var content = new MemoryStream(bytes))
                    {
                        return await migration.ImportAsync(step ?? throw new McpException("Give the step."), content, fileName, confirm, ct).ConfigureAwait(false);
                    }

                default:
                    throw new McpException("The action is steps, opening, template, export or import.");
            }
        });

    private static byte[] Bytes(string? base64)
    {
        if (string.IsNullOrWhiteSpace(base64))
        {
            throw new McpException("Give the file's content as base64.");
        }

        try
        {
            return Convert.FromBase64String(base64.Trim());
        }
        catch (FormatException)
        {
            throw new McpException("The content is not valid base64.");
        }
    }
}
