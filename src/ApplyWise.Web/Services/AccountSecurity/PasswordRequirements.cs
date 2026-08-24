using System.ComponentModel.DataAnnotations;

namespace ApplyWise.Web.Services.AccountSecurity;

public static class PasswordRequirements
{
    public const int MinimumLength = 12;
    public const int RequiredUniqueCharacters = 4;
    public static string UserFacingSummary =>
        $"Use {MinimumLength} or more characters with uppercase and lowercase letters, a number, and at least {RequiredUniqueCharacters} different characters.";
}

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter)]
public sealed class StrongPasswordAttribute : ValidationAttribute
{
    public StrongPasswordAttribute()
    {
        ErrorMessage = PasswordRequirements.UserFacingSummary;
    }

    public override bool IsValid(object? value)
    {
        if (value is not string password || password.Length < PasswordRequirements.MinimumLength)
        {
            return false;
        }

        return password.Any(char.IsUpper)
            && password.Any(char.IsLower)
            && password.Any(char.IsDigit)
            && password.Distinct().Take(PasswordRequirements.RequiredUniqueCharacters).Count()
                == PasswordRequirements.RequiredUniqueCharacters;
    }
}
