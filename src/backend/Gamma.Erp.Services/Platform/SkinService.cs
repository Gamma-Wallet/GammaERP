using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Gamma.Erp.Models.Platform;
using Gamma.Erp.Persistence;
using Gamma.Erp.Services.Entities;
using Microsoft.EntityFrameworkCore;

namespace Gamma.Erp.Services.Platform;

/// <summary>A skin as the Skins screen lists it.</summary>
public sealed record SkinDto(Guid Id, string? Name, string Kind, bool Dark, bool InUse, int Tokens);

/// <summary>A skin file, as it is uploaded and downloaded: its name, kind (erp or pos), whether it is dark, and its token values.</summary>
public sealed record SkinFile(string? Name, string? Kind, bool Dark, IReadOnlyDictionary<string, string>? Tokens);

/// <summary>The skin in use for a kind: its token values (none: the app's built-in look).</summary>
public sealed record SkinInUseDto(Guid? Id, string? Name, bool Dark, IReadOnlyDictionary<string, string> Tokens);

/// <summary>An uploaded skin and what the contrast check found (warnings, not refusals).</summary>
public sealed record SkinUploadDto(SkinDto Skin, IReadOnlyList<string> Warnings);

/// <summary>
/// Skins (the founder, 2026-11-04): a company's values for the screens' design tokens, one in use for the ERP and one for the point of sale, chosen by an
/// administrator. A skin only sets values: each is checked to be a plain CSS value (no links, no code), and its fonts must be ones the app ships. A token
/// it leaves out keeps the built-in value.
/// </summary>
public sealed partial class SkinService(ErpDbContext db, RequestContext request, Visibility visibility)
{
    private static readonly string[] Kinds = ["erp", "pos"];

    /// <summary>The fonts the app ships (its @fontsource packages) and the generic families: a skin can name only these.</summary>
    private static readonly HashSet<string> Fonts = new(StringComparer.OrdinalIgnoreCase)
    {
        "Inter", "Source Serif 4", "Newsreader", "Georgia", "Times New Roman", "system-ui", "-apple-system", "Segoe UI", "serif", "sans-serif", "monospace",
        "ui-monospace", "Menlo", "Consolas",
    };

    /// <summary>The pairs whose contrast is checked on upload: text token, its background token, the least ratio.</summary>
    private static readonly (string Text, string Background, double Least)[] Pairs =
    [
        ("ink", "surface", 4.5), ("ink-2", "surface", 4.5), ("muted", "surface", 4.5), ("link", "surface", 4.5), ("primary-ink", "primary", 4.5),
        ("topbar-ink", "topbar", 4.5), ("line-strong", "surface", 3),
    ];

    public async Task<IReadOnlyList<SkinDto>> ListAsync(CancellationToken ct)
    {
        var company = await AdministratorAsync(ct).ConfigureAwait(false);
        var rows = await db.Set<Skin>().AsNoTracking().Where(s => s.Active && s.OwnerId == company).OrderBy(s => s.Kind).ThenBy(s => s.Name).ToListAsync(ct).ConfigureAwait(false);
        return [.. rows.Select(Dto)];
    }

    /// <summary>The skin in use for a kind, for every signed-in user of the company.</summary>
    public async Task<SkinInUseDto> InUseAsync(string kind, CancellationToken ct)
    {
        var company = await visibility.CompanyAsync(ct).ConfigureAwait(false);
        var skin = company is null ? null : await db.Set<Skin>().AsNoTracking().FirstOrDefaultAsync(s => s.Active && s.InUse && s.OwnerId == company && s.Kind == kind, ct)
            .ConfigureAwait(false);
        return skin is null ? new SkinInUseDto(null, null, false, new Dictionary<string, string>()) : new SkinInUseDto(skin.Id, skin.Name, skin.Dark, TokensOf(skin));
    }

    /// <summary>A skin as a file, to download and change.</summary>
    public async Task<SkinFile> FileAsync(Guid id, CancellationToken ct)
    {
        var skin = await OwnAsync(id, ct).ConfigureAwait(false);
        return new SkinFile(skin.Name, skin.Kind, skin.Dark, TokensOf(skin));
    }

    /// <summary>Adds an uploaded skin, not yet in use; the contrast check's findings come back as warnings.</summary>
    public async Task<SkinUploadDto> UploadAsync(SkinFile file, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(file);
        var company = await AdministratorAsync(ct).ConfigureAwait(false);
        var kind = (file.Kind ?? "erp").Trim().ToLowerInvariant();
        var errors = new Dictionary<string, string>();
        if (string.IsNullOrWhiteSpace(file.Name) || file.Name.Trim().Length > 60)
        {
            errors["name"] = "A skin has a name of up to 60 characters.";
        }

        if (!Kinds.Contains(kind))
        {
            errors["kind"] = "A skin is for the ERP (erp) or the point of sale (pos).";
        }

        var tokens = file.Tokens ?? new Dictionary<string, string>();
        if (tokens.Count is 0 or > 300)
        {
            errors["tokens"] = "A skin sets between 1 and 300 tokens.";
        }

        foreach (var (name, value) in tokens)
        {
            if (Refusal(name, value) is { } why)
            {
                errors[$"tokens.{name}"] = why;
            }
        }

        if (errors.Count > 0)
        {
            throw new ValidationException(errors);
        }

        var skin = new Skin
        {
            Name = file.Name!.Trim(), Kind = kind, Dark = file.Dark, InUse = false, OwnerId = company, TenantId = request.TenantId,
            Tokens = JsonSerializer.Serialize(tokens.ToDictionary(t => t.Key.Trim().TrimStart('-').ToLowerInvariant(), t => t.Value.Trim())),
        };
        db.Add(skin);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return new SkinUploadDto(Dto(skin), Warnings(tokens));
    }

    /// <summary>Puts a skin in use for its kind; the one in use before is no longer.</summary>
    public async Task<SkinDto> UseAsync(Guid id, CancellationToken ct)
    {
        var skin = await OwnAsync(id, ct).ConfigureAwait(false);
        foreach (var other in await db.Set<Skin>().Where(s => s.Active && s.InUse && s.OwnerId == skin.OwnerId && s.Kind == skin.Kind && s.Id != id).ToListAsync(ct).ConfigureAwait(false))
        {
            other.InUse = false;
        }

        skin.InUse = true;
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Dto(skin);
    }

    /// <summary>No skin in use for a kind: the screens show the app's built-in look.</summary>
    public async Task UseBuiltInAsync(string kind, CancellationToken ct)
    {
        var company = await AdministratorAsync(ct).ConfigureAwait(false);
        foreach (var skin in await db.Set<Skin>().Where(s => s.Active && s.InUse && s.OwnerId == company && s.Kind == kind).ToListAsync(ct).ConfigureAwait(false))
        {
            skin.InUse = false;
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    /// <summary>Switches a skin off (nothing is deleted); the one in use stays until another is chosen.</summary>
    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        var skin = await OwnAsync(id, ct).ConfigureAwait(false);
        if (skin.InUse)
        {
            throw ValidationException.For("inUse", "This skin is in use: choose another one first.");
        }

        skin.Active = false;
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    /// <summary>Why a token can't be taken, or null. A value is a plain CSS value: colours, sizes, numbers, font names, shadows; no links or code.</summary>
    internal static string? Refusal(string name, string? value)
    {
        if (!TokenName().IsMatch(name.Trim().TrimStart('-')))
        {
            return "A token name is lowercase letters, digits and hyphens, such as surface or text-m.";
        }

        if (string.IsNullOrWhiteSpace(value) || value.Length > 200 || !PlainValue().IsMatch(value) || value.Contains("url(", StringComparison.OrdinalIgnoreCase)
            || value.Contains("expression", StringComparison.OrdinalIgnoreCase) || value.Contains("import", StringComparison.OrdinalIgnoreCase))
        {
            return "A value is a plain CSS value of up to 200 characters: a colour, a size, a number, font names or a shadow.";
        }

        if (name.StartsWith("font", StringComparison.OrdinalIgnoreCase) && !name.Contains("size", StringComparison.OrdinalIgnoreCase)
            && !name.Contains("weight", StringComparison.OrdinalIgnoreCase))
        {
            var unknown = value.Split(',').Select(f => f.Trim().Trim('\'', '"')).Where(f => f.Length > 0 && !Fonts.Contains(f)).ToList();
            if (unknown.Count > 0)
            {
                return $"The app does not ship {string.Join(", ", unknown)}: use {string.Join(", ", Fonts.Order())}.";
            }
        }

        return null;
    }

    /// <summary>The text and background pairs below their least contrast.</summary>
    internal static IReadOnlyList<string> Warnings(IReadOnlyDictionary<string, string> tokens)
    {
        var found = new List<string>();
        foreach (var (text, background, least) in Pairs)
        {
            if (tokens.TryGetValue(text, out var fore) && tokens.TryGetValue(background, out var back) && Colour(fore) is { } f && Colour(back) is { } b)
            {
                var ratio = Contrast(f, b);
                if (ratio < least)
                {
                    found.Add($"{text} on {background} has a contrast of {ratio.ToString("0.00", CultureInfo.InvariantCulture)}:1; at least {least.ToString("0.#", CultureInfo.InvariantCulture)}:1 is needed to read it easily.");
                }
            }
        }

        return found;
    }

    private static double Contrast((double R, double G, double B) a, (double R, double G, double B) b)
    {
        static double Lum((double R, double G, double B) c)
        {
            static double Channel(double v) => v <= 0.03928 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
            return 0.2126 * Channel(c.R) + 0.7152 * Channel(c.G) + 0.0722 * Channel(c.B);
        }

        var (la, lb) = (Lum(a), Lum(b));
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    /// <summary>A #rgb, #rrggbb or rgb(r g b) colour as 0–1 channels; null for anything else.</summary>
    private static (double R, double G, double B)? Colour(string value)
    {
        var v = value.Trim();
        if (Hex().Match(v) is { Success: true } hex)
        {
            var digits = hex.Groups[1].Value;
            if (digits.Length is 3 or 4)
            {
                digits = string.Concat(digits.Take(3).Select(c => $"{c}{c}"));
            }

            return (int.Parse(digits[..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture) / 255.0, int.Parse(digits[2..4], NumberStyles.HexNumber, CultureInfo.InvariantCulture) / 255.0,
                int.Parse(digits[4..6], NumberStyles.HexNumber, CultureInfo.InvariantCulture) / 255.0);
        }

        if (Rgb().Match(v) is { Success: true } rgb)
        {
            return (double.Parse(rgb.Groups[1].Value, CultureInfo.InvariantCulture) / 255, double.Parse(rgb.Groups[2].Value, CultureInfo.InvariantCulture) / 255,
                double.Parse(rgb.Groups[3].Value, CultureInfo.InvariantCulture) / 255);
        }

        return null;
    }

    private static Dictionary<string, string> TokensOf(Skin skin) =>
        JsonSerializer.Deserialize<Dictionary<string, string>>(skin.Tokens) ?? [];

    private static SkinDto Dto(Skin s) => new(s.Id, s.Name, s.Kind, s.Dark, s.InUse, TokensOf(s).Count);

    private async Task<Guid> AdministratorAsync(CancellationToken ct)
    {
        if (!request.IsAdministrator)
        {
            throw ValidationException.For("skin", $"{ErrorCodes.NotAllowedError}: only an administrator chooses the skins.");
        }

        return await visibility.CompanyAsync(ct).ConfigureAwait(false) ?? throw ValidationException.For("company", "Skins belong to a company: choose one of its nodes.");
    }

    private async Task<Skin> OwnAsync(Guid id, CancellationToken ct)
    {
        var company = await AdministratorAsync(ct).ConfigureAwait(false);
        return await db.Set<Skin>().FirstOrDefaultAsync(s => s.Id == id && s.Active && s.OwnerId == company, ct).ConfigureAwait(false)
            ?? throw ValidationException.For("id", "The company has no such skin.");
    }

    [GeneratedRegex("^[a-z][a-z0-9-]{0,59}$")]
    private static partial Regex TokenName();

    // Letters, digits, spaces and the punctuation colours, sizes, font lists and shadows need; never ; { } < > \ @ or quotes other than '.
    [GeneratedRegex(@"^[A-Za-z0-9 #.,%()'/+\-]+$")]
    private static partial Regex PlainValue();

    [GeneratedRegex("^#([0-9a-fA-F]{3}|[0-9a-fA-F]{4}|[0-9a-fA-F]{6}|[0-9a-fA-F]{8})$")]
    private static partial Regex Hex();

    [GeneratedRegex(@"^rgba?\(\s*([0-9.]+)[\s,]+([0-9.]+)[\s,]+([0-9.]+)")]
    private static partial Regex Rgb();
}
