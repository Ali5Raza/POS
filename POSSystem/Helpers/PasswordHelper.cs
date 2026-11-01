using Microsoft.AspNetCore.Identity;
using POSSystem.Models.Entities;

namespace POSSystem.Helpers;

public static class PasswordHelper
{
    private static readonly PasswordHasher<ApplicationUser> Hasher = new();

    public static string HashPassword(ApplicationUser user, string password)
    {
        return Hasher.HashPassword(user, password);
    }

    public static bool VerifyPassword(ApplicationUser user, string hashedPassword, string providedPassword)
    {
        if (string.IsNullOrEmpty(hashedPassword) || string.IsNullOrEmpty(providedPassword))
            return false;

        var result = Hasher.VerifyHashedPassword(user, hashedPassword, providedPassword);
        return result == PasswordVerificationResult.Success ||
               result == PasswordVerificationResult.SuccessRehashNeeded;
    }
}
