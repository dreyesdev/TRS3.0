using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TRS2._0.Models.DataModels;
using TRS2._0.Services;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using System.Security.Cryptography;
using System.Text;
using TRS2._0.Models.DataModels.TRS2._0.Models.DataModels;
using TRS2._0.Models.ViewModels;

namespace TRS2._0.Controllers
{
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

        [HttpGet]
        public IActionResult Login()
        {
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> Login(LoginViewModel model)
        {
            if (ModelState.IsValid)
            {
                var user = await _userManager.FindByNameAsync(model.BSCID);
                if (user == null)
                {
                    ModelState.AddModelError(string.Empty, "Your BSCID is incorrect or does not exist.");
                    return View(model);
                }

                var result = await _signInManager.PasswordSignInAsync(user, model.Password, model.RememberMe, false);
                if (result.Succeeded)
                {
                    _logger.LogInformation("User logged in.");
                    return RedirectToAction("Index", "Home");
                }

                ModelState.AddModelError(string.Empty, "Your password is incorrect.");
                _logger.LogWarning("Invalid login attempt.");
            }

            return View(model);
        }

        [HttpPost]
        public async Task<IActionResult> Logout()
        {
            await _signInManager.SignOutAsync();
            _logger.LogInformation("User logged out.");
            return RedirectToAction("Index", "Home");
        }

        [HttpGet]
        public IActionResult Register()
        {
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> Register(RegisterViewModel model)
        {
            if (ModelState.IsValid)
            {
                var personnel = await _context.Personnel.FirstOrDefaultAsync(p => p.BscId == model.BSCID);

                if (personnel == null)
                {
                    ModelState.AddModelError(string.Empty, "This user is not yet registered in our database. Please wait 24 hours or contact iss@bsc.es.");
                    return View(model);
                }

                if (!string.IsNullOrEmpty(personnel.Password))
                {
                    ModelState.AddModelError(string.Empty, "This user is already registered. Go to login page and click on 'I don't remember my password'.");
                    return View(model);
                }

                var password = GeneratePassword();

                // Actualizar la tabla personnel
                personnel.Password = password;
                _context.Update(personnel);
                await _context.SaveChangesAsync();

                // Crear el usuario en Identity
                var user = new ApplicationUser { UserName = model.BSCID, Email = personnel.Email, PersonnelId = personnel.Id };
                var result = await _userManager.CreateAsync(user, password);

                if (result.Succeeded)
                {
                    _logger.LogInformation("User created a new account with password.");

                    var code = await _userManager.GenerateEmailConfirmationTokenAsync(user);
                    var callbackUrl = Url.Action("ConfirmEmail", "Account", new { userId = user.Id, code = code }, protocol: HttpContext.Request.Scheme);

                    var emailContent = $@"
                    <html>
                    <body>
                        <p>Greetings, {personnel.Name},</p>
                        <p>You now have access to the TRS 3.0 web application, which is currently under development.</p>
                        <p>To access the site, please use the following link: <a href='https://opstrs03.bsc.es/'>TRS 3.0 - Beta</a></p>
                        <p>Your login credentials are:</p>
                        <ul>
                            <li><strong>Username:</strong> {model.BSCID}</li>
                            <li><strong>Password:</strong> {password}</li>
                        </ul>
                        <p>This access is intended to help us debug the web application before its final deployment.</p>
                        <p>Thank you very much for your collaboration.</p>
                        <p>Sincerely,</p>
                        <p>The ISS Team</p>
                    </body>
                    </html>";

                    await _emailSender.SendEmailAsync(personnel.Email, "Access to TRS 3.0 - Beta", emailContent);

                    _logger.LogInformation("User registration email sent.");

                    // Asignar rol al usuario
                    var roleService = new RoleService(_roleManager, _userManager);
                    await roleService.AssignRoleToUser(user.Id, "User"); // Asigna el rol "User" por defecto

                    return RedirectToAction("Index", "Home");
                }

                foreach (var error in result.Errors)
                {
                    ModelState.AddModelError(string.Empty, error.Description);
                }
            }

            _logger.LogWarning("Invalid registration attempt.");
            return View(model);
        }


        [HttpGet]
        public IActionResult ForgotPassword()
        {
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> ForgotPassword(ForgotPasswordViewModel model)
        {
            if (ModelState.IsValid)
            {
                var user = await _userManager.FindByEmailAsync(model.Email);
                if (user == null)
                {
                    return RedirectToAction(nameof(ForgotPasswordConfirmation));
                }

                var code = await _userManager.GeneratePasswordResetTokenAsync(user);
                var callbackUrl = Url.Action(nameof(ResetPassword), "Account", new { code }, protocol: HttpContext.Request.Scheme);
                await _emailSender.SendEmailAsync(model.Email, "Reset Password", $"Please reset your password by clicking <a href='{callbackUrl}'>here</a>. Your new password is {code}");

                _logger.LogInformation($"Password reset email sent to {model.Email}.");
                return RedirectToAction(nameof(ForgotPasswordConfirmation));
            }

            _logger.LogWarning("Invalid forgot password attempt.");
            return View(model);
        }

        [HttpGet]
        public IActionResult ResetPassword(string code = null)
        {
            return code == null ? View("Error") : View(new ResetPasswordViewModel { Code = code });
        }

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
                return RedirectToAction(nameof(ResetPasswordConfirmation));
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

        [HttpGet]
        public IActionResult ResetPasswordConfirmation()
        {
            return View();
        }

        [HttpGet]
        public IActionResult ChangePassword()
        {
            return View();
        }

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
                return RedirectToAction("ChangePasswordConfirmation");
            }

            foreach (var error in result.Errors)
            {
                ModelState.AddModelError(string.Empty, error.Description);
            }
            return View(model);
        }

        [HttpGet]
        public IActionResult ChangePasswordConfirmation()
        {
            return View();
        }

        private string GeneratePassword()
        {
            const int length = 12;
            const string valid = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ1234567890";
            const string nonAlphanumeric = "!@#$%^&*()-_=+<>,.?/";

            StringBuilder res = new StringBuilder();
            Random rnd = new Random();

            // Ensure at least one non-alphanumeric character and one digit
            res.Append(nonAlphanumeric[rnd.Next(nonAlphanumeric.Length)]);
            res.Append(valid[rnd.Next(10, 36)]); // Adds a digit

            for (int i = 2; i < length; i++)
            {
                res.Append(valid[rnd.Next(valid.Length)]);
            }

            return res.ToString();
        }

        private string HashPassword(string password)
        {
            using (var sha256 = SHA256.Create())
            {
                var bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(password));
                var builder = new StringBuilder();
                foreach (var t in bytes)
                {
                    builder.Append(t.ToString("x2"));
                }
                return builder.ToString();
            }
        }

        public IActionResult ForgotPasswordConfirmation()
        {
            return View();
        }
    }
}
