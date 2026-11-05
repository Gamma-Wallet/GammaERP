using System.Globalization;
using System.Text;
using Gamma.Erp.Api.Auth;
using Gamma.Erp.Models.Base;
using Gamma.Erp.Models.CostCenters;
using Gamma.Erp.Models.Users;
using Gamma.Erp.Persistence;
using Gamma.Erp.Seed;
using Gamma.Erp.Services;
using Microsoft.EntityFrameworkCore;

namespace Gamma.Erp.Api.Setup;

/// <summary>A user the guided start creates: the name to sign in with, the password, and whether the user administers everything.</summary>
public sealed record StartUser(string Username, string Password, bool Administrator = false);

/// <summary>
/// A sign-up with everything the guided start asked, step by step (the founder's decisions, 2026-10-29: a user name and a password only,
/// applied at the end in one go): the owner's sign-in, the country, the company, the structure (a group when <see cref="CorporateName"/> is
/// given, else a single company that is its own root), its activity and whether to take the activity's configuration from the catalogue,
/// the pack groups to load (all when none are named), the cash desks and bank accounts, and further users.
/// </summary>
public sealed record SignUpRequest(string Username, string Password, string Country, string CompanyName, string? CompanyCode = null, string? CorporateName = null,
    string Activity = ActivityCatalogue.General, bool FromCatalogue = true, IReadOnlyCollection<string>? Groups = null, IReadOnlyList<string>? CashDesks = null,
    IReadOnlyList<string>? Banks = null, IReadOnlyList<StartUser>? Users = null);

/// <summary>What a sign-up made: the corporate (tenant), the root, the company, the owner, and the configuration rows loaded.</summary>
public sealed record SignUpResult(Guid TenantId, Guid Root, Guid Company, Guid Owner, int Rows);

/// <summary>
/// A company added to a group: its name, country and activity, and where its configuration comes from: the country's pack, or another
/// company of the group (<see cref="CopyFrom"/>), in both cases only the groups named (all when none are).
/// </summary>
public sealed record CompanyRequest(string Name, string Country, string? Code = null, string Activity = ActivityCatalogue.General, bool FromCatalogue = true,
    IReadOnlyCollection<string>? Groups = null, Guid? CopyFrom = null, IReadOnlyList<string>? CashDesks = null, IReadOnlyList<string>? Banks = null);

/// <summary>What adding a company made: the company and the configuration rows it got.</summary>
public sealed record CompanyResult(Guid Company, int Rows);

/// <summary>The country whose pack gives an existing group its books, and which of its parts.</summary>
public sealed record GroupBooksRequest(string Country, IReadOnlyCollection<string>? Groups = null);

/// <summary>A country a company can start in: its code, base currency, the languages of its pack and the activity profiles it has.</summary>
public sealed record SetupCountry(string Code, string? Currency, IReadOnlyList<string> Languages, IReadOnlyList<string> Profiles);

/// <summary>
/// Sign-up and the guided start: a new corporate with its first company and its users, everything in one transaction, so a sign-up either
/// makes all of it or nothing; and later companies of a group, from the pack or copied from another company of the group.
/// </summary>
public sealed class SetupService(ErpDbContext db)
{
    /// <summary>The shortest password accepted.</summary>
    public const int MinPasswordLength = 12;

    private const string CashDeskType = "cost_center_type:CASH_DESK";
    private const string BankType = "cost_center_type:BANK";

    /// <summary>The countries with a pack, which a sign-up can start in.</summary>
    public static IReadOnlyList<SetupCountry> Countries() =>
        [.. StarterPacks.Countries.Select(c => new SetupCountry(c, StarterPacks.BaseCurrency(c), StarterPacks.Languages(c), StarterPacks.Profiles(c)))];

    /// <summary>Whether a user name is still free (as it would be kept: trimmed, lower case).</summary>
    public async Task<bool> UsernameFreeAsync(string username, CancellationToken ct)
    {
        var name = SignInService.Normalize(username);
        return name.Length > 0 && !await db.Set<User>().AsNoTracking().AnyAsync(u => u.Username == name, ct).ConfigureAwait(false);
    }

    public async Task<SignUpResult> SignUpAsync(SignUpRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        var users = new List<StartUser> { new(request.Username, request.Password, Administrator: true) };
        users.AddRange(request.Users ?? []);
        await CheckUsersAsync(users, ct).ConfigureAwait(false);
        Check(request.Country, request.CompanyName, request.Activity, request.Groups, needsPack: true);
        var setup = new CompanySetup(CodeFor(request.CompanyCode, request.CompanyName), request.CompanyName.Trim(), request.Country.ToUpperInvariant(),
            CostCenters(request.CashDesks, request.Banks), request.Activity, request.FromCatalogue, request.Groups);

        var tenantId = Guid.CreateVersion7();
        await using var transaction = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
        var organisation = string.IsNullOrWhiteSpace(request.CorporateName)
            ? Organisation.CreateCompany(db, tenantId, setup)
            : Organisation.Create(db, tenantId, request.CorporateName.Trim(), setup.Country, [setup]);
        var rows = Organisation.SetUpCompany(db, organisation, setup);
        if (organisation.Corporate != organisation.Companies[setup.Code])
        {
            // A group keeps its own books too, from the same country's pack.
            rows += Organisation.SetUpGroup(db, organisation, setup.Country, setup.Groups);
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        var company = organisation.Companies[setup.Code];
        rows += await Organisation.CompleteCostCentersAsync(db, company, ct).ConfigureAwait(false);
        rows += await Organisation.LinkGroupPartnersAsync(db, organisation, ct).ConfigureAwait(false);
        // The first users' type and roles come from the company's pack (named in its language, owned by the company); a pack without them
        // gets them made here, owned by the company.
        var staff = await db.Set<UserType>().FirstOrDefaultAsync(t => t.Active && t.Code == "STAFF" && t.OwnerId == company, ct).ConfigureAwait(false)
            ?? db.Add(new UserType { Code = "STAFF", Name = TranslatedText.English("Staff"), IsElement = true, TenantId = tenantId, OwnerId = company }).Entity;
        var administrators = await db.Set<UserCategory>().FirstOrDefaultAsync(c => c.Active && c.IsAdministrator && c.IsElement && c.OwnerId == company, ct).ConfigureAwait(false)
            ?? db.Add(new UserCategory { Code = "ADMINISTRATOR", Name = TranslatedText.English("Administrator"), IsElement = true, IsAdministrator = true, TenantId = tenantId,
                OwnerId = company }).Entity;
        var others = await db.Set<UserCategory>().FirstOrDefaultAsync(c => c.Active && c.Code == "USERS" && c.OwnerId == company, ct).ConfigureAwait(false)
            ?? db.Add(new UserCategory { Code = "USERS", Name = TranslatedText.English("Users"), IsElement = true, TenantId = tenantId, OwnerId = company }).Entity;
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        var made = new List<Guid>();
        foreach (var (start, index) in users.Select((u, i) => (u, i)))
        {
            var name = SignInService.Normalize(start.Username);
            var user = new User { TypeId = staff.Id, Code = index == 0 ? "OWNER" : $"USER{index}", Name = TranslatedText.English(start.Username.Trim()), Username = name,
                PasswordHash = SignInService.Hash(start.Password), TenantId = tenantId };
            db.Add(user);
            try
            {
                await db.SaveChangesAsync(ct).ConfigureAwait(false);
            }
            catch (DbUpdateException taken) when (taken.InnerException is Npgsql.PostgresException { SqlState: Npgsql.PostgresErrorCodes.UniqueViolation })
            {
                // Taken between the check and now, by another sign-up (the unique index decides).
                throw ValidationException.For("username", $"{ErrorCodes.SetupError}: the user name {name} is taken.");
            }

            // The owner works at the root (the corporate, or the company itself); the others at the company.
            db.AddRange(new InterUserCategory { ElementId = user.Id, CategoryId = start.Administrator ? administrators.Id : others.Id, TenantId = tenantId },
                new InterUserCostCenter { UserId = user.Id, CostCenterId = index == 0 ? organisation.Corporate : company, TenantId = tenantId });
            made.Add(user.Id);
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await transaction.CommitAsync(ct).ConfigureAwait(false);
        return new SignUpResult(tenantId, organisation.Corporate, company, made[0], rows);
    }

    /// <summary>Whether the group the request works at still has no books of its own (no document types yet): it then needs <see cref="SetUpGroupBooksAsync"/>.</summary>
    public async Task<bool> GroupBooksNeededAsync(RequestContext context, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(context);
        var organisation = await Organisation.FindAsync(db, context.TenantId, ct).ConfigureAwait(false);
        return organisation is not null && context.NodeId == organisation.Corporate
            && !await db.Set<Models.Documents.DocumentType>().AsNoTracking().AnyAsync(t => t.OwnerId == organisation.Corporate, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Gives an existing group its own books once (groups signed up before 2026-10-31 have none): the country's pack, owned by the group.
    /// Only an administrator working at the group.
    /// </summary>
    public async Task<int> SetUpGroupBooksAsync(GroupBooksRequest request, RequestContext context, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);
        var organisation = await Organisation.FindAsync(db, context.TenantId, ct).ConfigureAwait(false)
            ?? throw ValidationException.For("company", $"{ErrorCodes.SetupError}: a single company keeps its books already.");
        if (!context.IsAdministrator || context.NodeId != organisation.Corporate)
        {
            throw ValidationException.For("rights", $"{ErrorCodes.NotAllowedError}: only an administrator working at the group sets up its books.");
        }

        if (!await GroupBooksNeededAsync(context, ct).ConfigureAwait(false))
        {
            throw ValidationException.For("company", $"{ErrorCodes.SetupError}: the group keeps its books already.");
        }

        Check(request.Country, "group", ActivityCatalogue.General, request.Groups, needsPack: true);
        await using var transaction = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
        var rows = Organisation.SetUpGroup(db, organisation, request.Country, request.Groups);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        rows += await Organisation.LinkGroupPartnersAsync(db, organisation, ct).ConfigureAwait(false);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await transaction.CommitAsync(ct).ConfigureAwait(false);
        return rows;
    }

    /// <summary>Makes the group and its companies each other's partners where they are not yet (an administrator working at the group); safe to repeat.</summary>
    public async Task<int> LinkGroupPartnersAsync(RequestContext context, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(context);
        var organisation = await Organisation.FindAsync(db, context.TenantId, ct).ConfigureAwait(false)
            ?? throw ValidationException.For("company", $"{ErrorCodes.SetupError}: a single company has no group.");
        if (!context.IsAdministrator || context.NodeId != organisation.Corporate)
        {
            throw ValidationException.For("rights", $"{ErrorCodes.NotAllowedError}: only an administrator working at the group links its partners.");
        }

        var rows = await Organisation.LinkGroupPartnersAsync(db, organisation, ct).ConfigureAwait(false);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return rows;
    }

    /// <summary>Adds a company to the group of the administrator asking, who works at the corporate.</summary>
    public async Task<CompanyResult> AddCompanyAsync(CompanyRequest request, RequestContext context, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);
        var organisation = await Organisation.FindAsync(db, context.TenantId, ct).ConfigureAwait(false)
            ?? throw ValidationException.For("company", $"{ErrorCodes.SetupError}: a single company has no corporate above it, so no company can be added.");
        if (!context.IsAdministrator || context.NodeId != organisation.Corporate)
        {
            throw ValidationException.For("rights", $"{ErrorCodes.NotAllowedError}: only an administrator working at the corporate adds companies.");
        }

        Check(request.Country, request.Name, request.Activity, request.Groups, needsPack: request.CopyFrom is null);
        if (request.CopyFrom is { } from && !organisation.Companies.Values.Contains(from))
        {
            throw ValidationException.For("copyFrom", $"{ErrorCodes.SetupError}: the company to copy from is not a company of this group.");
        }

        var code = CodeFor(request.Code, request.Name);
        if (organisation.Companies.ContainsKey(code))
        {
            throw ValidationException.For("code", $"{ErrorCodes.SetupError}: the group already has a company {code}.");
        }

        var setup = new CompanySetup(code, request.Name.Trim(), request.Country.ToUpperInvariant(), null, request.Activity, request.FromCatalogue, request.Groups);
        await using var transaction = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
        var company = await Organisation.AddCompanyAsync(db, organisation, setup, ct).ConfigureAwait(false);
        var withCompany = organisation with { Companies = new Dictionary<string, Guid>(organisation.Companies) { [code] = company } };
        var rows = request.CopyFrom is { } source
            ? await CompanyCopy.AddAsync(db, context.TenantId, source, company, request.Groups, ct).ConfigureAwait(false)
            : Organisation.SetUpCompany(db, withCompany, setup);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        // The cash desks and bank accounts, of the company's own types (from its pack or copied).
        var types = await db.Set<CostCenterType>().AsNoTracking().Where(t => t.OwnerId == company && (t.Code == "CASH_DESK" || t.Code == "BANK"))
            .ToDictionaryAsync(t => t.Code!, t => t.Id, ct).ConfigureAwait(false);
        var language = StarterPacks.Language(setup.Country);
        foreach (var (typeKey, centre, name) in CostCenters(request.CashDesks, request.Banks))
        {
            var type = typeKey == CashDeskType ? "CASH_DESK" : "BANK";
            if (!types.TryGetValue(type, out var typeId))
            {
                throw ValidationException.For(typeKey == CashDeskType ? "cashDesks" : "banks", $"{ErrorCodes.SetupError}: the company's configuration has no {type} type.");
            }

            db.Add(new CostCenter { IdSup = company, TypeId = typeId, Code = centre, Name = TranslatedText.Both(name, language), TenantId = context.TenantId, OwnerId = company });
            rows++;
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        rows += await Organisation.CompleteCostCentersAsync(db, company, ct).ConfigureAwait(false);
        rows += await Organisation.LinkGroupPartnersAsync(db, withCompany, ct).ConfigureAwait(false);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await transaction.CommitAsync(ct).ConfigureAwait(false);
        return new CompanyResult(company, rows);
    }

    private async Task CheckUsersAsync(List<StartUser> users, CancellationToken ct)
    {
        var names = users.Select(u => SignInService.Normalize(u.Username)).ToList();
        if (names.FirstOrDefault(n => n.Length is < 3 or > 64 || !n.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-')) is { } bad)
        {
            throw ValidationException.For("username", $"{ErrorCodes.SetupError}: a user name has 3 to 64 letters, digits, dots, dashes or underscores (\"{bad}\" has not).");
        }

        if (names.GroupBy(n => n).FirstOrDefault(g => g.Count() > 1) is { } twice)
        {
            throw ValidationException.For("username", $"{ErrorCodes.SetupError}: the user name {twice.Key} is given twice.");
        }

        if (users.Any(u => (u.Password ?? string.Empty).Length < MinPasswordLength))
        {
            throw ValidationException.For("password", $"{ErrorCodes.SetupError}: a password has at least {MinPasswordLength} characters.");
        }

        var taken = await db.Set<User>().AsNoTracking().Where(u => names.Contains(u.Username!)).Select(u => u.Username).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (taken is not null)
        {
            throw ValidationException.For("username", $"{ErrorCodes.SetupError}: the user name {taken} is taken.");
        }
    }

    private static void Check(string country, string name, string activity, IReadOnlyCollection<string>? groups, bool needsPack)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw ValidationException.For("name", $"{ErrorCodes.SetupError}: the company needs a name.");
        }

        if (string.IsNullOrWhiteSpace(country) || country.Trim().Length != 2)
        {
            throw ValidationException.For("country", $"{ErrorCodes.SetupError}: the country is its two-letter ISO code.");
        }

        if (needsPack && !StarterPacks.Countries.Contains(country.ToUpperInvariant(), StringComparer.Ordinal))
        {
            throw ValidationException.For("country", $"{ErrorCodes.SetupError}: there is no starter pack for {country.ToUpperInvariant()}; copy the configuration from another company.");
        }

        try
        {
            ActivityCatalogue.PathOf(activity);
            StarterPacks.Chosen(groups);
        }
        catch (ArgumentException wrong)
        {
            throw ValidationException.For(wrong.ParamName == "groups" ? "groups" : "activity", $"{ErrorCodes.SetupError}: {wrong.Message.Split(" (Parameter")[0]}");
        }
    }

    /// <summary>The company's code: the one given, or its name's letters and digits (without accents), upper case, at most 12.</summary>
    private static string CodeFor(string? code, string name)
    {
        var source = string.IsNullOrWhiteSpace(code) ? name : code;
        var plain = new StringBuilder();
        foreach (var c in source.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark && char.IsAsciiLetterOrDigit(c))
            {
                plain.Append(char.ToUpperInvariant(c));
            }
        }

        return plain.Length == 0 ? "COMPANY" : plain.ToString(0, Math.Min(plain.Length, 12));
    }

    private static List<(string TypeKey, string Code, string Name)> CostCenters(IReadOnlyList<string>? cashDesks, IReadOnlyList<string>? banks) =>
    [
        .. (cashDesks ?? []).Where(n => !string.IsNullOrWhiteSpace(n)).Select((n, i) => (CashDeskType, $"CASH{i + 1}", n.Trim())),
        .. (banks ?? []).Where(n => !string.IsNullOrWhiteSpace(n)).Select((n, i) => (BankType, $"BANK{i + 1}", n.Trim())),
    ];
}
