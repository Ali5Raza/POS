using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using POSSystem.Data;

namespace POSSystem.Authorization;

public class PermissionRequirement : IAuthorizationRequirement
{
    public string PermissionCode { get; }

    public PermissionRequirement(string permissionCode)
    {
        PermissionCode = permissionCode;
    }
}

public class RequirePermissionAttribute : AuthorizeAttribute
{
    public const string PolicyPrefix = "PERMISSION_";

    public RequirePermissionAttribute(string permissionCode)
    {
        Policy = $"{PolicyPrefix}{permissionCode}";
    }
}

public class PermissionPolicyProvider : IAuthorizationPolicyProvider
{
    private readonly DefaultAuthorizationPolicyProvider _fallbackPolicyProvider;

    public PermissionPolicyProvider(IOptions<AuthorizationOptions> options)
    {
        _fallbackPolicyProvider = new DefaultAuthorizationPolicyProvider(options);
    }

    public Task<AuthorizationPolicy> GetDefaultPolicyAsync() =>
        _fallbackPolicyProvider.GetDefaultPolicyAsync();

    public Task<AuthorizationPolicy?> GetFallbackPolicyAsync() =>
        _fallbackPolicyProvider.GetFallbackPolicyAsync();

    public Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        if (policyName.StartsWith(RequirePermissionAttribute.PolicyPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var permissionCode = policyName.Substring(RequirePermissionAttribute.PolicyPrefix.Length);
            var policy = new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .AddRequirements(new PermissionRequirement(permissionCode))
                .Build();
            return Task.FromResult<AuthorizationPolicy?>(policy);
        }

        return _fallbackPolicyProvider.GetPolicyAsync(policyName);
    }
}

public class PermissionAuthorizationHandler : AuthorizationHandler<PermissionRequirement>
{
    private readonly IServiceProvider _serviceProvider;

    public PermissionAuthorizationHandler(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        PermissionRequirement requirement)
    {
        if (context.User.Identity?.IsAuthenticated != true)
            return;

        // Admin always has full permissions
        if (context.User.IsInRole("Admin"))
        {
            context.Succeed(requirement);
            return;
        }

        // Check claim first for fast path
        if (context.User.HasClaim("Permission", requirement.PermissionCode))
        {
            context.Succeed(requirement);
            return;
        }

        // Also check database so dynamic role permission updates work immediately
        var userIdStr = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (int.TryParse(userIdStr, out int userId))
        {
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var hasPerm = await db.Users
                .AsNoTracking()
                .Where(u => u.Id == userId && u.IsActive)
                .SelectMany(u => u.Role!.RolePermissions)
                .AnyAsync(rp => rp.Permission!.Code == requirement.PermissionCode);

            if (hasPerm)
            {
                context.Succeed(requirement);
            }
        }
    }
}

public static class ClaimsPrincipalExtensions
{
    public static int? GetUserId(this ClaimsPrincipal user)
    {
        var val = user.FindFirstValue(ClaimTypes.NameIdentifier);
        return int.TryParse(val, out var id) ? id : null;
    }

    public static string GetUsername(this ClaimsPrincipal user)
    {
        return user.Identity?.Name ?? "System";
    }

    public static string GetFullName(this ClaimsPrincipal user)
    {
        return user.FindFirstValue("FullName") ?? user.Identity?.Name ?? "User";
    }

    public static bool HasPermission(this ClaimsPrincipal user, string permissionCode)
    {
        if (user.Identity?.IsAuthenticated != true) return false;
        if (user.IsInRole("Admin")) return true;
        return user.HasClaim("Permission", permissionCode);
    }
}
