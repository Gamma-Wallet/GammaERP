using Microsoft.EntityFrameworkCore;
using Gamma.Erp.Services;

namespace Gamma.Erp.Api.Setup;

/// <summary>
/// A new customer's company and its first administrator, made on the server: <c>create-company --username x --country AL --company "Name"</c>, the password in
/// ERP_ADMIN_PASSWORD so it stays out of the shell's history. There is no public way in: that administrator makes every other user.
/// <para>Its open banking provider is asked for at the same time and may be left out (the founder, 2026-11-14): <c>--bank-provider EnableBanking
/// --bank-app-id &lt;id&gt; --bank-key-file &lt;app.pem&gt; --bank-redirect &lt;url&gt; [--bank-environment Sandbox|Production] [--bank-access-days 180]</c>.
/// The key is read from the file, never from the command line.</para>
/// </summary>
public static class CreateCompanyCommand
{
    public const string Name = "create-company";

    /// <summary>The name of the setting holding the first administrator's password.</summary>
    public const string PasswordSetting = "ERP_ADMIN_PASSWORD";

    /// <summary>Makes the company; the result is the process's exit code.</summary>
    public static async Task<int> RunAsync(SetupService setup, Gamma.Erp.Persistence.ErpDbContext db, IReadOnlyList<string> args, string? password, TextWriter output,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(setup);
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(output);
        string? Option(string name)
        {
            var at = args.ToList().IndexOf($"--{name}");
            return at >= 0 && at + 1 < args.Count ? args[at + 1] : null;
        }

        var (username, country, company) = (Option("username"), Option("country"), Option("company"));
        // No password given: one is made here (the founder, 2026-11-04: a first password per installation, shown once, changed at the first sign-in).
        var generated = string.IsNullOrEmpty(password);
        password = generated ? FirstPassword() : password;
        if (username is null || country is null || company is null)
        {
            await output.WriteLineAsync($"Usage: {Name} --username <name> --country <code> --company <name> [--code <code>] [--corporate <group name>] [--activity <code>] {BankUsage}, with the password in {PasswordSetting}.").ConfigureAwait(false);
            return 2;
        }

        Services.Banking.BankProviderInput? bank = null;
        if (Option("bank-provider") is { } kind)
        {
            var keyFile = Option("bank-key-file");
            if (keyFile is null || !File.Exists(keyFile))
            {
                await output.WriteLineAsync($"Refused: --bank-key-file must name the application's .pem file ({BankUsage}).").ConfigureAwait(false);
                return 2;
            }

            int? days = int.TryParse(Option("bank-access-days"), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var d) ? d : null;
            bank = new Services.Banking.BankProviderInput(kind, Option("bank-app-id") ?? string.Empty, await File.ReadAllTextAsync(keyFile, ct).ConfigureAwait(false),
                Option("bank-redirect"), Option("bank-environment"), days);
        }

        try
        {
            var request = new SignUpRequest(username, password!, country.ToUpperInvariant(), company, Option("code"), Option("corporate"), Option("activity") ?? Seed.ActivityCatalogue.General,
                BankProvider: bank);
            var made = await setup.SignUpAsync(request, ct).ConfigureAwait(false);
            await output.WriteLineAsync($"Company {company} made, {made.Rows} configuration rows loaded. {username} is its administrator.").ConfigureAwait(false);
            await output.WriteLineAsync(bank is null ? "Open banking: not set (Configuration › Open banking, later)." : $"Open banking: {bank.Kind}, application {bank.ApplicationId}, key sealed.").ConfigureAwait(false);
            // The administrator changes this password at the first sign-in; the first Claude token works until it is switched off. Both are shown only now.
            var entity = db.Model.FindEntityType(typeof(Gamma.Erp.Models.Users.User))!;
            var sql = $"UPDATE \"{entity.GetSchema()}\".\"{entity.GetTableName()}\" SET must_change_password = TRUE WHERE id = {{0}}";
            await db.Database.ExecuteSqlRawAsync(sql, [made.Owner], ct).ConfigureAwait(false);
            var token = await Auth.TokenService.MakeForAsync(db, made.TenantId, made.Owner, "Claude (first)", ct).ConfigureAwait(false);
            if (generated)
            {
                await output.WriteLineAsync($"First password: {password}   (it must be changed at the first sign-in)").ConfigureAwait(false);
            }

            await output.WriteLineAsync($"First Claude token: {token.Value}   (shown only now; make others or switch it off from the account menu, Claude tokens)").ConfigureAwait(false);
            return 0;
        }
        catch (ValidationException refused)
        {
            await output.WriteLineAsync($"Refused: {string.Join(" ", refused.Errors.Values)}").ConfigureAwait(false);
            return 1;
        }
    }

    private const string BankUsage = "[--bank-provider EnableBanking --bank-app-id <id> --bank-key-file <app.pem> --bank-redirect <url> [--bank-environment Sandbox|Production] [--bank-access-days <1-180>]]";

    /// <summary>A first password of 16 letters and digits, without look-alikes (0/O, 1/l/I).</summary>
    private static string FirstPassword()
    {
        const string letters = "abcdefghjkmnpqrstuvwxyzABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        return string.Concat(System.Security.Cryptography.RandomNumberGenerator.GetItems<char>(letters, 16));
    }
}
