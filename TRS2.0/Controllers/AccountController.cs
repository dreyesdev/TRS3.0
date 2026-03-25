using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TRS2._0.Models;
using TRS2._0.Models.DataModels;
using TRS2._0.Models.DataModels.TRS2._0.Models.DataModels;
using TRS2._0.Models.ViewModels;
using TRS2._0.Services;

namespace TRS2._0.Controllers
{
    /// <summary>
    /// Handles the authentication lifecycle for TRS users, including registration, credential recovery and administrative resets.
    /// </summary>
    public class AccountController : Controller
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly SignInManager<ApplicationUser> _signInManager;
        private readonly TRSDBContext _context;
        private readonly IEmailSender _emailSender;
        private readonly ILogger<AccountController> _logger;
        private readonly RoleManager<IdentityRole> _roleManager;

        public AccountController(
            UserManager<ApplicationUser> userManager,
            SignInManager<ApplicationUser> signInManager,
            TRSDBContext context,
            IEmailSender emailSender,
            ILogger<AccountController> logger,
            RoleManager<IdentityRole> roleManager)
        {
            _userManager = userManager;
            _signInManager = signInManager;
            _context = context;
            _emailSender = emailSender;
            _logger = logger;
            _roleManager = roleManager;
        }

        /// <summary>
        /// Displays the login form.
        /// </summary>
        [HttpGet]
        public IActionResult Login()
        {
            return View();
        }

        /// <summary>
        /// Authenticates the user and records the first login of the day for personnel-linked accounts.
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> Login(LoginViewModel model)
        {
            if (!ModelState.IsValid)
            {
                return View(model);
            }

            var user = await _userManager.FindByNameAsync(model.BSCID);
            if (user == null)
            {
                ModelState.AddModelError(string.Empty, "Your BSCID is incorrect or does not exist.");
                return View(model);
            }

            var isAdmin = await _userManager.IsInRoleAsync(user, "Admin");
            int? personId = null;

            if (!isAdmin)
            {
                personId = await ResolvePersonnelIdAsync(model.BSCID);
                if (!personId.HasValue)
                {
                    ModelState.AddModelError(string.Empty, "No personnel record found for this user.");
                    return View(model);
                }
            }

            var result = await _signInManager.PasswordSignInAsync(user, model.Password, model.RememberMe, false);
            if (!result.Succeeded)
            {
                ModelState.AddModelError(string.Empty, "Your password is incorrect.");
                _logger.LogWarning("Invalid login attempt for user {UserName}.", model.BSCID);
                return View(model);
            }

            _logger.LogInformation("User {UserName} logged in at {Timestamp}.", user.UserName, DateTime.UtcNow);

            if (personId.HasValue)
            {
                await RegisterDailyLoginIfNeededAsync(personId.Value, user.UserName ?? model.BSCID);
            }

            return RedirectToAction("Index", "Home");
        }

        /// <summary>
        /// Signs the current user out of the application.
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> Logout()
        {
            await _signInManager.SignOutAsync();
            _logger.LogInformation("User logged out.");
            return RedirectToAction("Index", "Home");
        }

        /// <summary>
        /// Displays the self-registration form.
        /// </summary>
        [HttpGet]
        public IActionResult Register()
        {
            return View();
        }

        /// <summary>
        /// Creates a new identity account for a personnel record that already exists in the operational database.
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> Register(RegisterViewModel model)
        {
            if (!ModelState.IsValid)
            {
                _logger.LogWarning("Invalid registration attempt.");
                return View(model);
            }

            var personnel = await _context.Personnel.FirstOrDefaultAsync(p => p.BscId == model.BSCID);
            if (personnel == null)
            {
                ModelState.AddModelError(
                    string.Empty,
                    "This user is not yet registered in our database. Please wait 24 hours or contact iss@bsc.es.");
                return View(model);
            }

            var existingUser = await _userManager.FindByNameAsync(model.BSCID);
            if (existingUser != null)
            {
                ModelState.AddModelError(
                    string.Empty,
                    "This user already has an account. Please go to the login page and use 'I don't remember my password'.");
                return View(model);
            }

            var password = await GenerateValidPasswordAsync();
            personnel.Password = password;
            _context.Update(personnel);
            await _context.SaveChangesAsync();

            var user = new ApplicationUser
            {
                UserName = model.BSCID,
                Email = personnel.Email,
                PersonnelId = personnel.Id
            };

            var result = await _userManager.CreateAsync(user, password);
            if (!result.Succeeded)
            {
                foreach (var error in result.Errors)
                {
                    ModelState.AddModelError(string.Empty, error.Description);
                }

                _logger.LogWarning("Invalid registration attempt.");
                return View(model);
            }

            _logger.LogInformation("User created a new account with password.");

            await SendRegistrationEmailAsync(personnel, model.BSCID, password);
            await AssignDefaultRoleAsync(user);

            return RedirectToAction(nameof(RegisterConfirmation));
        }

        /// <summary>
        /// Displays the password recovery form.
        /// </summary>
        [HttpGet]
        public IActionResult ForgotPassword()
        {
            return View();
        }

        /// <summary>
        /// Sends the password reset link when the provided email address exists in Identity.
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> ForgotPassword(ForgotPasswordViewModel model)
        {
            if (!ModelState.IsValid)
            {
                _logger.LogWarning("Invalid forgot password attempt.");
                return View(model);
            }

            var user = await _userManager.FindByEmailAsync(model.Email);
            if (user == null)
            {
                return RedirectToAction(nameof(Login));
            }

            var code = await _userManager.GeneratePasswordResetTokenAsync(user);
            var callbackUrl = Url.Action(nameof(ResetPassword), "Account", new { code }, protocol: HttpContext.Request.Scheme);

            await _emailSender.SendEmailAsync(
                model.Email,
                "Reset Password",
                $"Please reset your password by clicking <a href='{callbackUrl}'>here</a>");

            _logger.LogInformation("Password reset email sent to {Email}.", model.Email);
            return RedirectToAction(nameof(ForgotPasswordConfirmation));
        }

        /// <summary>
        /// Displays the password reset form when a valid reset code is present.
        /// </summary>
        [HttpGet]
        public IActionResult ResetPassword(string? code = null)
        {
            return code == null
                ? View("Error")
                : View(new ResetPasswordViewModel { Code = code });
        }

        /// <summary>
        /// Applies a password reset requested through the recovery workflow.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ResetPassword(ResetPasswordViewModel model)
        {
            if (!ModelState.IsValid)
            {
                return View(model);
            }

            var user = await _userManager.FindByEmailAsync(model.Email);
            if (user == null)
            {
                return RedirectToAction(nameof(Login));
            }

            var result = await _userManager.ResetPasswordAsync(user, model.Code, model.Password);
            if (result.Succeeded)
            {
                return RedirectToAction(nameof(ResetPasswordConfirmation));
            }

            foreach (var error in result.Errors)
            {
                ModelState.AddModelError(string.Empty, error.Description);
            }

            return View(model);
        }

        /// <summary>
        /// Displays the confirmation page after a successful password reset.
        /// </summary>
        [HttpGet]
        public IActionResult ResetPasswordConfirmation()
        {
            return View();
        }

        /// <summary>
        /// Displays the change-password form for authenticated users.
        /// </summary>
        [HttpGet]
        public IActionResult ChangePassword()
        {
            return View();
        }

        /// <summary>
        /// Updates the password of the current authenticated user.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ChangePassword(ChangePasswordViewModel model)
        {
            if (!ModelState.IsValid)
            {
                return View(model);
            }

            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return RedirectToAction(nameof(Login));
            }

            var result = await _userManager.ChangePasswordAsync(user, model.OldPassword, model.NewPassword);
            if (result.Succeeded)
            {
                _logger.LogInformation("User changed their password successfully.");
                await _signInManager.RefreshSignInAsync(user);
                return RedirectToAction(nameof(ChangePasswordConfirmation));
            }

            foreach (var error in result.Errors)
            {
                ModelState.AddModelError(string.Empty, error.Description);
            }

            return View(model);
        }

        /// <summary>
        /// Displays the confirmation page after a successful password change.
        /// </summary>
        [HttpGet]
        public IActionResult ChangePasswordConfirmation()
        {
            return View();
        }

        /// <summary>
        /// Resets a user password from the admin tools and notifies the linked personnel record by email.
        /// </summary>
        [HttpPost]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> AdminResetPassword(string userId)
        {
            var user = await _userManager.FindByIdAsync(userId);
            if (user == null)
            {
                return Json(new { success = false, message = "User not found." });
            }

            var personnel = await _context.Personnel.FirstOrDefaultAsync(p => p.Id == user.PersonnelId);
            if (personnel == null)
            {
                return Json(new { success = false, message = "Linked personnel not found." });
            }

            var password = await GenerateValidPasswordAsync();
            var token = await _userManager.GeneratePasswordResetTokenAsync(user);

            await _context.Entry(user).ReloadAsync();

            var result = await _userManager.ResetPasswordAsync(user, token, password);
            if (!result.Succeeded)
            {
                var errorMessages = string.Join("; ", result.Errors.Select(e => e.Description));
                return Json(new
                {
                    success = false,
                    message = $"Password reset failed: {errorMessages}",
                    password
                });
            }

            personnel.Password = password;
            _context.Update(personnel);
            await _context.SaveChangesAsync();

            try
            {
                await SendAdminResetPasswordEmailAsync(user, personnel, password);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending reset password email to user {UserName}.", user.UserName);
                return Json(new
                {
                    success = false,
                    message = $"Password reset succeeded, but failed to send email. Error: {ex.Message}",
                    password
                });
            }

            return Json(new
            {
                success = true,
                message = "Password reset and email sent successfully.",
                password
            });
        }

        /// <summary>
        /// Displays the password recovery confirmation view.
        /// </summary>
        public IActionResult ForgotPasswordConfirmation()
        {
            return View();
        }

        /// <summary>
        /// Displays the self-registration confirmation view.
        /// </summary>
        [HttpGet]
        public IActionResult RegisterConfirmation()
        {
            return View();
        }

        private async Task<int?> ResolvePersonnelIdAsync(string bscId)
        {
            var personId = await _context.Personnel
                .Where(p => p.BscId == bscId)
                .Select(p => (int?)p.Id)
                .FirstOrDefaultAsync();

            return personId > 0 ? personId : null;
        }

        private async Task RegisterDailyLoginIfNeededAsync(int personId, string userName)
        {
            var today = DateTime.UtcNow.Date;
            var hasLoggedToday = await _context.UserLoginHistories
                .AnyAsync(l => l.PersonId == personId && l.LoginTime.Date == today);

            if (hasLoggedToday)
            {
                _logger.LogInformation(
                    "User {UserName} already logged in today. No duplicate record created.",
                    userName);
                return;
            }

            _context.UserLoginHistories.Add(new UserLoginHistory
            {
                PersonId = personId,
                LoginTime = DateTime.UtcNow
            });

            await _context.SaveChangesAsync();
        }

        private async Task SendRegistrationEmailAsync(Personnel personnel, string bscId, string password)
        {
            var emailContent = $@"
<html>
<body>
    <p>Greetings, {personnel.Name},</p>
    <p>You now have access to the TRS 3.0 web application.</p>
    <p>
        To access the site, please use the following link:
        <a href='https://opstrs03.bsc.es/'>TRS 3.0</a>
    </p>
    <p>Your login credentials are:</p>
    <ul>
        <li><strong>Username:</strong> <code style='font-family: monospace;'>{bscId}</code></li>
        <li><strong>Password:</strong> <code style='font-family: monospace;'>{password}</code></li>
    </ul>
    <p><strong style='color: darkred;'>Important:</strong> please copy and paste the password directly to avoid typos.</p>
    <p>If you experience issues logging in, try using a private/incognito window in your browser.</p>
    <p>
        If the problem persists, please contact us at
        <a href='mailto:iss@bsc.es'>iss@bsc.es</a>.
    </p>
    <p>Thank you for your collaboration.</p>
    <p>Sincerely,</p>
    <p>The ISS Team</p>
</body>
</html>";

            await _emailSender.SendEmailAsync(personnel.Email, "Access to TRS 3.0", emailContent);
            _logger.LogInformation("User registration email sent.");
        }

        private async Task AssignDefaultRoleAsync(ApplicationUser user)
        {
            var roleService = new RoleService(_roleManager, _userManager);
            var roleResult = await roleService.AssignRoleToUser(user.Id, "Researcher");

            if (!roleResult.Succeeded)
            {
                var errors = string.Join(", ", roleResult.Errors.Select(e => e.Description));
                _logger.LogError("Error assigning role to user {UserName}: {Errors}", user.UserName, errors);
            }
        }

        private async Task SendAdminResetPasswordEmailAsync(
            ApplicationUser user,
            Personnel personnel,
            string password)
        {
            var emailContent = $@"
<html>
<body>
    <p>Hello {personnel.Name},</p>
    <p>Your password for TRS 3.0 has been reset by an administrator.</p>
    <p><strong>Username:</strong> {user.UserName}</p>
    <p><strong>New Password:</strong> {password}</p>
    <p>Please log in using the following link: <a href='https://opstrs03.bsc.es/'>TRS 3.0</a></p>
    <p>If you experience any issues, please contact <a href='mailto:iss@bsc.es'>iss@bsc.es</a>.</p>
</body>
</html>";

            await _emailSender.SendEmailAsync(
                personnel.Email,
                "Your TRS 3.0 Password Has Been Reset",
                emailContent);
        }

        private async Task<string> GenerateValidPasswordAsync()
        {
            const int length = 12;
            const string uppercase = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
            const string lowercase = "abcdefghijklmnopqrstuvwxyz";
            const string digits = "0123456789";
            const string specialChars = "!@#$%^&*()-_=+,.?/";

            var allChars = uppercase + lowercase + digits + specialChars;
            var random = new Random();

            while (true)
            {
                var password =
                    uppercase[random.Next(uppercase.Length)].ToString() +
                    lowercase[random.Next(lowercase.Length)] +
                    digits[random.Next(digits.Length)] +
                    specialChars[random.Next(specialChars.Length)];

                for (var index = 4; index < length; index++)
                {
                    password += allChars[random.Next(allChars.Length)];
                }

                password = new string(password.ToCharArray().OrderBy(_ => random.Next()).ToArray());

                if (await ValidatePasswordAsync(new ApplicationUser(), password))
                {
                    return password;
                }
            }
        }

        private async Task<bool> ValidatePasswordAsync(ApplicationUser user, string password)
        {
            var validators = _userManager.PasswordValidators;
            foreach (var validator in validators)
            {
                var result = await validator.ValidateAsync(_userManager, user, password);
                if (!result.Succeeded)
                {
                    return false;
                }
            }

            return true;
        }
    }
}
