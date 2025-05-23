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
using TRS2._0.Models;
using Microsoft.AspNetCore.Authorization;

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

                // Verificar si el usuario tiene el rol de administrador
                var isAdmin = await _userManager.IsInRoleAsync(user, "Admin");

                int? personId = null;

                // Solo buscar el PersonId si el usuario NO es administrador
                if (!isAdmin)
                {
                    personId = await _context.Personnel
                        .Where(p => p.BscId == model.BSCID)
                        .Select(p => p.Id)
                        .FirstOrDefaultAsync();

                    if (personId == 0) // Si no se encuentra en Personnel
                    {
                        ModelState.AddModelError(string.Empty, "No personnel record found for this user.");
                        return View(model);
                    }
                }

                var result = await _signInManager.PasswordSignInAsync(user, model.Password, model.RememberMe, false);
                if (result.Succeeded)
                {
                    _logger.LogInformation("User {0} logged in at {1}.", user.UserName, DateTime.UtcNow);

                    // Si no es administrador, registrar el login en la base de datos solo si NO existe un registro para hoy
                    if (!isAdmin)
                    {
                        var today = DateTime.UtcNow.Date;

                        bool hasLoggedToday = await _context.UserLoginHistories
                            .AnyAsync(l => l.PersonId == personId.Value && l.LoginTime.Date == today);

                        if (!hasLoggedToday) // Solo registrar si no hay un registro previo hoy
                        {
                            var loginRecord = new UserLoginHistory
                            {
                                PersonId = personId.Value,
                                LoginTime = DateTime.UtcNow
                            };

                            _context.UserLoginHistories.Add(loginRecord);
                            await _context.SaveChangesAsync();
                        }
                        else
                        {
                            _logger.LogInformation("User {0} already logged in today. No duplicate record created.", user.UserName);
                        }
                    }

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
                
                // Buscar persona en base de datos
                var personnel = await _context.Personnel.FirstOrDefaultAsync(p => p.BscId == model.BSCID);
                if (personnel == null)
                {
                    ModelState.AddModelError(string.Empty, "This user is not yet registered in our database. Please wait 24 hours or contact iss@bsc.es.");
                    return View(model);
                }

                // Comprobar si ya está registrado en Identity
                var existingUser = await _userManager.FindByNameAsync(model.BSCID);
                if (existingUser != null)
                {
                    ModelState.AddModelError(string.Empty, "This user already has an account. Please go to the login page and use 'I don't remember my password'.");
                    return View(model);
                }

                var password = await GenerateValidPasswordAsync();


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

                    string emailContent = $@"
                                            <html>
                                            <body>
                                                <p>Greetings, {personnel.Name},</p>
                                                <p>You now have access to the TRS 3.0 web application.</p>
                                                <p>To access the site, please use the following link: <a href='https://opstrs03.bsc.es/'>TRS 3.0</a></p>
                                                <p>Your login credentials are:</p>
                                                <ul>
                                                    <li><strong>Username:</strong> {model.BSCID}</li>
                                                    <li><strong>Password:</strong> {password}</li>
                                                </ul>
                                                <p>If you experience any issues while logging in or using the system, please feel free to contact us at <a href='mailto:iss@bsc.es'>iss@bsc.es</a>.</p>
                                                <p>Thank you for your collaboration.</p>
                                                <p>Sincerely,</p>
                                                <p>The ISS Team</p>
                                            </body>
                                            </html>";


                    await _emailSender.SendEmailAsync(personnel.Email, "Access to TRS 3.0", emailContent);

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
                    return RedirectToAction(nameof(Login)); // Redirigir en lugar de mostrar vista de confirmación
                }

                var code = await _userManager.GeneratePasswordResetTokenAsync(user);
                var callbackUrl = Url.Action(nameof(ResetPassword), "Account", new { code }, protocol: HttpContext.Request.Scheme);
                await _emailSender.SendEmailAsync(model.Email, "Reset Password",
                    $"Please reset your password by clicking <a href='{callbackUrl}'>here</a>");

                _logger.LogInformation($"Password reset email sent to {model.Email}.");
                return RedirectToAction(nameof(Login)); // Se elimina ForgotPasswordConfirmation
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
                return RedirectToAction(nameof(Login)); // Redirigir en lugar de mostrar vista de confirmación
            }

            var result = await _userManager.ResetPasswordAsync(user, model.Code, model.Password);
            if (result.Succeeded)
            {
                return RedirectToAction(nameof(Login)); // Se elimina ResetPasswordConfirmation
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

            // Refrescar el user para evitar problemas de concurrencia
            await _context.Entry(user).ReloadAsync();

            var result = await _userManager.ResetPasswordAsync(user, token, password);

            if (!result.Succeeded)
            {
                var errorMessages = string.Join("; ", result.Errors.Select(e => e.Description));
                return Json(new
                {
                    success = false,
                    message = $"Password reset failed: {errorMessages}",
                    password = password
                });
            }

            // ✅ Guardar la nueva contraseña en Personnel
            personnel.Password = password;
            _context.Update(personnel);
            await _context.SaveChangesAsync();

            try
            {
                string emailContent = $@"
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

                await _emailSender.SendEmailAsync(personnel.Email, "Your TRS 3.0 Password Has Been Reset", emailContent);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending reset password email to user {UserName}", user.UserName);

                return Json(new
                {
                    success = false,
                    message = $"Password reset succeeded, but failed to send email. Error: {ex.Message}",
                    password = password
                });
            }

            return Json(new
            {
                success = true,
                message = "Password reset and email sent successfully.",
                password = password
            });
        }




        private async Task<string> GenerateValidPasswordAsync()
        {
            const int length = 12;
            const string uppercase = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
            const string lowercase = "abcdefghijklmnopqrstuvwxyz";
            const string digits = "0123456789";
            const string specialChars = "!@#$%^&*()-_=+<>,.?/";

            string allChars = uppercase + lowercase + digits + specialChars;
            Random rnd = new Random();

            while (true)
            {
                string password =
                    uppercase[rnd.Next(uppercase.Length)].ToString() +
                    lowercase[rnd.Next(lowercase.Length)] +
                    digits[rnd.Next(digits.Length)] +
                    specialChars[rnd.Next(specialChars.Length)];

                for (int i = 4; i < length; i++)
                {
                    password += allChars[rnd.Next(allChars.Length)];
                }

                // Mezclar caracteres
                password = new string(password.ToCharArray().OrderBy(x => rnd.Next()).ToArray());

                // Validar usando el validador real de Identity
                var tempUser = new ApplicationUser(); // Usuario temporal
                if (await ValidatePasswordAsync(tempUser, password))
                {
                    return password;
                }

                // Si no es válido, se genera de nuevo (while loop)
            }
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
