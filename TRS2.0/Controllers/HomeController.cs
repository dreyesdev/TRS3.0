using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;
using TRS2._0.Models;

namespace TRS2._0.Controllers
{
    public class HomeController : Controller
    {
        private readonly ILogger<HomeController> _logger;

        public HomeController(ILogger<HomeController> logger)
        {
            _logger = logger;
        }

        public IActionResult Index()
        {
            return View();
        }

        public IActionResult Privacy()
        {
            return View();
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }

        public IActionResult Welcome()
        {
            return View();
        }

    }

    [Route("diagnostic")]
    public class DiagnosticController : Controller
    {
        private readonly IConfiguration _configuration;

        public DiagnosticController(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        [HttpGet("db")]
        public IActionResult GetDatabaseConnection()
        {
            var connectionString = _configuration.GetConnectionString("DefaultConnection");
            return Ok($"Base de datos en uso: {connectionString}");
        }
    }
}