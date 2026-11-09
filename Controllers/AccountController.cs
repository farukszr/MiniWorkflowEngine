using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using MiniWorkflowEngine.Domain.Entities;
using MiniWorkflowEngine.Services;
using MiniWorkflowEngine.ViewModels;

namespace MiniWorkflowEngine.Controllers;

[ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
public sealed class AccountController(SignInManager<ApplicationUser> signInManager,
    UserManager<ApplicationUser> userManager, PasswordRecoveryService recovery) : Controller
{
    [AllowAnonymous, HttpGet]
    public IActionResult Login(string? returnUrl = null)
    {
        if (User.Identity?.IsAuthenticated == true) return RedirectToAction("Index", "Home");
        return View(new LoginViewModel { ReturnUrl = Url.IsLocalUrl(returnUrl) ? returnUrl : null });
    }

    [AllowAnonymous, HttpPost]
    public async Task<IActionResult> Login(LoginViewModel model)
    {
        model.ReturnUrl = Url.IsLocalUrl(model.ReturnUrl) ? model.ReturnUrl : null;
        if (!ModelState.IsValid) return View(model);

        var user = await userManager.FindByEmailAsync(model.Email.Trim());
        var result = user is null ? Microsoft.AspNetCore.Identity.SignInResult.Failed
            : await signInManager.PasswordSignInAsync(user, model.Password, model.RememberMe, lockoutOnFailure: true);
        if (result.Succeeded)
            return model.ReturnUrl is not null ? LocalRedirect(model.ReturnUrl) : RedirectToAction("Index", "Home");

        // Hesabın varlığı, kilit durumu veya parola hakkında ayrı bilgi verilmez.
        ModelState.AddModelError(string.Empty, "Giriş yapılamadı. Bilgilerinizi kontrol edin; çok sayıda başarısız denemeden sonra 15 dakika bekleyin.");
        model.Password = string.Empty;
        ModelState.Remove(nameof(model.Password));
        return View(model);
    }

    [Authorize, HttpPost]
    public async Task<IActionResult> Logout()
    {
        await signInManager.SignOutAsync();
        return RedirectToAction(nameof(Login));
    }

    [AllowAnonymous, HttpGet]
    public IActionResult ResetPassword() => View(new ResetPasswordViewModel());

    [AllowAnonymous, HttpPost, EnableRateLimiting("PasswordRecovery")]
    public async Task<IActionResult> ResetPassword(ResetPasswordViewModel model)
    {
        if (ModelState.IsValid)
        {
            var result = await recovery.ResetAsync(model);
            if (result.Succeeded)
            {
                await signInManager.SignOutAsync();
                TempData["Success"] = "Şifreniz güncellendi. Yeni şifrenizle giriş yapabilirsiniz.";
                return RedirectToAction(nameof(Login));
            }
            foreach (var error in result.Errors) ModelState.AddModelError(string.Empty, error.Description);
        }
        model.Code = model.Password = model.ConfirmPassword = "";
        return View(model);
    }

    [AllowAnonymous, HttpGet]
    public IActionResult AccessDenied()
    {
        Response.StatusCode = StatusCodes.Status403Forbidden;
        return View();
    }
}
