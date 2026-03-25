using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;
using TRS2._0.Models;

namespace TRS2._0.Controllers
{
    /// <summary>
    /// Serves the public landing pages and shared MVC views that are not tied to a specific business area.
    /// </summary>
    public class HomeController : Controller
    {
        /// <summary>
        /// Displays the application home page.
        /// </summary>
        public IActionResult Index()
        {
            return View();
        }

        /// <summary>
        /// Displays the privacy information page.
        /// </summary>
        public IActionResult Privacy()
        {
            return View();
        }

        /// <summary>
        /// Renders the standard error view with the current request identifier.
        /// </summary>
        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel
            {
                RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier
            });
        }

        /// <summary>
        /// Displays the application welcome page.
        /// </summary>
        public IActionResult Welcome()
        {
            return View();
        }
    }

    /// <summary>
    /// Exposes simple diagnostic endpoints used to validate runtime infrastructure configuration.
    /// </summary>
    [Route("diagnostic")]
    public class DiagnosticController : Controller
    {
        private readonly IConfiguration _configuration;

        public DiagnosticController(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        /// <summary>
        /// Returns the configured default connection string so support teams can verify the active database target.
        /// </summary>
        [HttpGet("db")]
        public IActionResult GetDatabaseConnection()
        {
            var connectionString = _configuration.GetConnectionString("DefaultConnection");
            return Ok($"Base de datos en uso: {connectionString}");
        }
    }
}
