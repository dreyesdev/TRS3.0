using Microsoft.AspNetCore.Mvc;

namespace TRS2._0.Controllers
{
    /// <summary>
    /// Centralizes error views that are rendered outside the standard controller flows.
    /// </summary>
    public class ErrorController : Controller
    {
        /// <summary>
        /// Displays the access denied view used by authorization failures.
        /// </summary>
        [Route("Error/AccessDenied")]
        public IActionResult AccessDenied()
        {
            return View();
        }
    }
}
