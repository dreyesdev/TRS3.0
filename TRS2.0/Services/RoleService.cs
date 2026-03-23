using Microsoft.AspNetCore.Identity;
using TRS2._0.Models.DataModels;

namespace TRS2._0.Services
{
    /// <summary>
    /// Coordinates application role assignments through ASP.NET Identity.
    /// </summary>
    public class RoleService
    {
        private readonly RoleManager<IdentityRole> _roleManager;
        private readonly UserManager<ApplicationUser> _userManager;

        public RoleService(RoleManager<IdentityRole> roleManager, UserManager<ApplicationUser> userManager)
        {
            _roleManager = roleManager;
            _userManager = userManager;
        }

        /// <summary>
        /// Assigns an existing role to an existing user.
        /// </summary>
        public async Task<IdentityResult> AssignRoleToUser(string userId, string roleName)
        {
            var user = await _userManager.FindByIdAsync(userId);
            if (user is null)
            {
                return IdentityResult.Failed(new IdentityError { Description = "User not found." });
            }

            var roleExists = await _roleManager.RoleExistsAsync(roleName);
            if (!roleExists)
            {
                return IdentityResult.Failed(new IdentityError { Description = $"Role '{roleName}' does not exist." });
            }

            return await _userManager.AddToRoleAsync(user, roleName);
        }
    }
}
