using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace ApplyWise.Web.Services.Admin;

public static class OwnerProvisioningCommand
{
    public const string Command = "--provision-owner";
    public const string CompleteExistingFlag = "--complete-existing-owner";

    public static bool IsRequested(string[] args) =>
        args.Any(argument => string.Equals(argument, Command, StringComparison.Ordinal));

    public static Task<int> RunAsync(
        IServiceProvider services,
        IHostEnvironment environment,
        string[] args,
        CancellationToken cancellationToken = default) =>
        RunAsync(
            services,
            environment,
            args,
            ReadSecretFromConsole,
            Console.Out,
            cancellationToken);

    public static async Task<int> RunAsync(
        IServiceProvider services,
        IHostEnvironment environment,
        string[] args,
        Func<string, string> readSecret,
        TextWriter output,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(readSecret);
        ArgumentNullException.ThrowIfNull(output);

        if (!environment.IsProduction())
        {
            throw new InvalidOperationException(
                "Owner provisioning must run with ASPNETCORE_ENVIRONMENT=Production against the reviewed production configuration.");
        }

        var unknownArguments = args.Where(argument =>
                !string.Equals(argument, Command, StringComparison.Ordinal)
                && !string.Equals(argument, CompleteExistingFlag, StringComparison.Ordinal)
                && !argument.StartsWith("--email=", StringComparison.Ordinal))
            .ToArray();
        if (unknownArguments.Length > 0)
        {
            throw new InvalidOperationException(
                $"Unsupported owner provisioning argument: {unknownArguments[0]}");
        }

        var options = services.GetRequiredService<IOptions<AdminAccessOptions>>().Value;
        var configuredEmails = options.ValidEmails().ToArray();
        var requestedEmail = args
            .SingleOrDefault(argument => argument.StartsWith("--email=", StringComparison.Ordinal))?
            ["--email=".Length..]
            .Trim();
        var email = !string.IsNullOrWhiteSpace(requestedEmail)
            ? requestedEmail
            : configuredEmails.Length == 1
                ? configuredEmails[0]
                : throw new InvalidOperationException(
                    "Specify exactly one configured owner with --email=<address>.");
        if (!options.Contains(email))
        {
            throw new InvalidOperationException(
                "The requested address is not present in AdminAccess:Emails.");
        }

        var users = services.GetRequiredService<UserManager<IdentityUser>>();
        var adminRoles = services.GetRequiredService<IAdminRoleAssignmentService>();
        var user = await users.FindByEmailAsync(email);
        if (user is not null
            && user.EmailConfirmed
            && await users.GetTwoFactorEnabledAsync(user)
            && await users.IsInRoleAsync(user, AdminAccess.Role))
        {
            await output.WriteLineAsync(
                "The configured owner is already confirmed, MFA-enabled, and assigned the administrator role.");
            return 0;
        }

        var completeExisting = args.Contains(CompleteExistingFlag, StringComparer.Ordinal);
        if (user is not null && !completeExisting)
        {
            throw new InvalidOperationException(
                "An incomplete account already uses this owner address. Audit its origin, then rerun with --complete-existing-owner only if it is trusted.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        var password = readSecret("Initial owner password");
        if (string.IsNullOrWhiteSpace(password))
        {
            throw new InvalidOperationException("A non-empty owner password is required.");
        }

        if (user is null)
        {
            user = new IdentityUser
            {
                UserName = email,
                Email = email,
                EmailConfirmed = true
            };
            EnsureSucceeded(
                await users.CreateAsync(user, password),
                "The owner identity could not be created");
        }
        else
        {
            user.EmailConfirmed = true;
            EnsureSucceeded(
                await users.UpdateAsync(user),
                "The existing owner identity could not be confirmed");
            var passwordResult = await users.HasPasswordAsync(user)
                ? await users.ResetPasswordAsync(
                    user,
                    await users.GeneratePasswordResetTokenAsync(user),
                    password)
                : await users.AddPasswordAsync(user, password);
            EnsureSucceeded(passwordResult, "The owner password could not be set");
        }

        await adminRoles.SynchronizeUserAsync(user);
        if (!await users.IsInRoleAsync(user, AdminAccess.Role))
        {
            throw new InvalidOperationException(
                "The owner identity was created but the administrator role could not be assigned.");
        }

        EnsureSucceeded(
            await users.ResetAuthenticatorKeyAsync(user),
            "The owner authenticator key could not be generated");
        var authenticatorKey = await users.GetAuthenticatorKeyAsync(user)
            ?? throw new InvalidOperationException(
                "The owner authenticator key was not available after generation.");
        var issuer = Uri.EscapeDataString("ApplyWise");
        var account = Uri.EscapeDataString(email);
        var encodedKey = Uri.EscapeDataString(authenticatorKey);
        await output.WriteLineAsync("Add this account to a trusted authenticator application:");
        await output.WriteLineAsync($"Authenticator key: {authenticatorKey}");
        await output.WriteLineAsync(
            $"Authenticator URI: otpauth://totp/{issuer}:{account}?secret={encodedKey}&issuer={issuer}&digits=6");

        var verificationCode = readSecret("Current six-digit authenticator code")
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal);
        if (!await users.VerifyTwoFactorTokenAsync(
                user,
                TokenOptions.DefaultAuthenticatorProvider,
                verificationCode))
        {
            throw new InvalidOperationException(
                "The authenticator code was not valid. MFA remains disabled; rerun with --complete-existing-owner after checking the account.");
        }

        EnsureSucceeded(
            await users.SetTwoFactorEnabledAsync(user, true),
            "MFA could not be enabled for the owner");
        var recoveryCodes = (await users.GenerateNewTwoFactorRecoveryCodesAsync(user, 10))?
            .ToArray() ?? [];
        if (recoveryCodes.Length == 0)
        {
            throw new InvalidOperationException(
                "MFA was enabled but recovery codes could not be generated.");
        }

        await output.WriteLineAsync("Store these one-time recovery codes offline; they will not be shown again:");
        foreach (var recoveryCode in recoveryCodes)
        {
            await output.WriteLineAsync(recoveryCode);
        }
        await output.WriteLineAsync(
            "Owner provisioning completed. Clear this terminal, remove temporary database access, sign in, and verify /health/ready before accepting traffic.");
        return 0;
    }

    private static void EnsureSucceeded(IdentityResult result, string message)
    {
        if (result.Succeeded) return;
        var errorCodes = string.Join(", ", result.Errors.Select(error => error.Code));
        throw new InvalidOperationException($"{message}. Identity errors: {errorCodes}.");
    }

    private static string ReadSecretFromConsole(string prompt)
    {
        if (Console.IsInputRedirected || Console.IsOutputRedirected)
        {
            throw new InvalidOperationException(
                "Owner credentials and MFA secrets must be entered in a private interactive terminal, not redirected output or CI logs.");
        }

        Console.Write($"{prompt}: ");
        var value = new StringBuilder();
        while (Console.ReadKey(intercept: true) is var key && key.Key != ConsoleKey.Enter)
        {
            if (key.Key == ConsoleKey.Backspace)
            {
                if (value.Length > 0) value.Length--;
                continue;
            }
            if (!char.IsControl(key.KeyChar)) value.Append(key.KeyChar);
        }
        Console.WriteLine();
        return value.ToString();
    }
}
