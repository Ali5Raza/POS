using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using POSSystem.Authorization;
using POSSystem.Data;
using POSSystem.Helpers;
using POSSystem.Models.ViewModels;
using POSSystem.Services;

namespace POSSystem.Controllers;

public class AccountController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly IAuditService _auditService;

    public AccountController(ApplicationDbContext context, IAuditService auditService)
    {
        _context = context;
        _auditService = auditService;
    }

    [HttpGet]
    [AllowAnonymous]
    public IActionResult Login(string? returnUrl = null)
    {
        if (User.Identity?.IsAuthenticated == true)
            return RedirectToAction("Index", "Dashboard");

        return View(new LoginViewModel { ReturnUrl = returnUrl });
    }

    [HttpPost]
    [AllowAnonymous]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginViewModel model)
    {
        if (!ModelState.IsValid)
            return View(model);

        var username = model.Username.Trim().ToLower();
        var user = await _context.Users
            .Include(u => u.Role)
                .ThenInclude(r => r!.RolePermissions)
                    .ThenInclude(rp => rp.Permission)
            .FirstOrDefaultAsync(u => u.Username.ToLower() == username);

        if (user == null || !PasswordHelper.VerifyPassword(user, user.PasswordHash, model.Password))
        {
            ModelState.AddModelError(string.Empty, "Invalid username or password.");
            await _auditService.LogAsync(null, model.Username, "FailedLogin", "ApplicationUser", null, null, "Invalid credentials attempt", HttpContext.Connection.RemoteIpAddress?.ToString());
            return View(model);
        }

        if (!user.IsActive)
        {
            ModelState.AddModelError(string.Empty, "Your user account has been deactivated. Please contact the Administrator.");
            return View(model);
        }

        user.LastLogin = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.Username),
            new("FullName", user.FullName),
            new(ClaimTypes.Email, user.Email),
            new(ClaimTypes.Role, user.Role?.Name ?? "Cashier")
        };

        if (user.Role?.RolePermissions != null)
        {
            foreach (var rp in user.Role.RolePermissions)
            {
                if (rp.Permission != null)
                {
                    claims.Add(new Claim("Permission", rp.Permission.Code));
                }
            }
        }

        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        var principal = new ClaimsPrincipal(identity);

        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            principal,
            new AuthenticationProperties
            {
                IsPersistent = model.RememberMe,
                ExpiresUtc = model.RememberMe ? DateTimeOffset.UtcNow.AddDays(7) : DateTimeOffset.UtcNow.AddHours(8)
            });

        await _auditService.LogAsync(user.Id, user.Username, "Login", "ApplicationUser", user.Id.ToString(),
            null, $"User '{user.Username}' ({user.Role?.Name}) signed in", HttpContext.Connection.RemoteIpAddress?.ToString());

        if (!string.IsNullOrWhiteSpace(model.ReturnUrl) && Url.IsLocalUrl(model.ReturnUrl))
            return Redirect(model.ReturnUrl);

        if (user.Role?.Name == "Cashier")
            return RedirectToAction("Index", "POS");

        return RedirectToAction("Index", "Dashboard");
    }

    [HttpPost]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        var userId = User.GetUserId();
        var username = User.GetUsername();

        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        await _auditService.LogAsync(userId, username, "Logout", "ApplicationUser", userId?.ToString(),
            null, $"User '{username}' logged out", HttpContext.Connection.RemoteIpAddress?.ToString());

        return RedirectToAction("Login", "Account");
    }

    [HttpGet]
    [Authorize]
    public IActionResult ChangePassword()
    {
        return View(new ChangePasswordViewModel());
    }

    [HttpPost]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ChangePassword(ChangePasswordViewModel model)
    {
        if (!ModelState.IsValid)
            return View(model);

        var userId = User.GetUserId();
        var user = await _context.Users.FindAsync(userId);
        if (user == null)
            return RedirectToAction("Login");

        if (!PasswordHelper.VerifyPassword(user, user.PasswordHash, model.CurrentPassword))
        {
            ModelState.AddModelError("CurrentPassword", "Current password is incorrect.");
            return View(model);
        }

        user.PasswordHash = PasswordHelper.HashPassword(user, model.NewPassword);
        await _context.SaveChangesAsync();

        await _auditService.LogAsync(user.Id, user.Username, "ChangePassword", "ApplicationUser", user.Id.ToString(),
            null, "User changed own password", HttpContext.Connection.RemoteIpAddress?.ToString());

        TempData["SuccessMessage"] = "Password changed successfully.";
        return RedirectToAction("Index", "Dashboard");
    }

    [HttpGet]
    [AllowAnonymous]
    public IActionResult AccessDenied()
    {
        Response.StatusCode = 403;
        return View("~/Views/Error/Status.cshtml", (403, "Access Denied", "You do not have the required permission to perform this action or view this resource."));
    }
}

public class ErrorController : Controller
{
    [Route("Error/{statusCode:int}")]
    [AllowAnonymous]
    public IActionResult HttpStatusCodeHandler(int statusCode, string? message = null)
    {
        Response.StatusCode = statusCode;
        var (title, defaultDesc) = statusCode switch
        {
            400 => ("400 - Bad Request", "The request could not be completed due to invalid input or a business rule constraint."),
            401 => ("401 - Unauthorized", "Please sign in to access this page."),
            403 => ("403 - Forbidden", "You do not have permission to perform this operation."),
            404 => ("404 - Page Not Found", "The requested resource or record could not be found."),
            _ => ("500 - Internal Server Error", "An unexpected error occurred while processing your request. Technical details have been logged.")
        };

        return View("~/Views/Error/Status.cshtml", (statusCode, title, string.IsNullOrWhiteSpace(message) ? defaultDesc : message));
    }
}
