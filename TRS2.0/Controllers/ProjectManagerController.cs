using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using TRS2._0.Services;
using TRS2._0.Models.DataModels;
using System.IO;
using System.Net.Mail;
using System.Threading.Tasks;
using System.Net;
using System.Collections.Generic;
using System;
using OfficeOpenXml;
using Serilog;
using System.Globalization;
using System.Linq;
using TRS2._0.Models.ViewModels.ProjectManager;
using TRS2._0.Models.ViewModels;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Authorization;
using TRS2._0.Models.DataModels.TRS2._0.Models.DataModels;

[Authorize(Roles = "ProjectManager,Researcher,Admin")]
public class ProjectManagerController : Controller
{
    private readonly WorkCalendarService _workCalendarService;
    private readonly ILogger<ProjectManagerController> _logger;
    private readonly TRSDBContext _context;
    private readonly IConfiguration _configuration;
    private readonly UserManager<ApplicationUser> _userManager;

    public ProjectManagerController(WorkCalendarService workCalendarService, ILogger<ProjectManagerController> logger, TRSDBContext context, IConfiguration configuration, UserManager<ApplicationUser> userManager)
    {
        _workCalendarService = workCalendarService;
        _logger = logger;
        _context = context;
        _configuration = configuration;
        _userManager = userManager;
    }

    public async Task<IActionResult> Index()
    {
        var errores = await _context.TimesheetErrorLogs
    .Select(e => new TimesheetErrorLog
    {
        Id = e.Id,
        FileName = e.FileName,
        PersonName = e.PersonName,
        ProjectName = e.ProjectName,
        WorkPackageName = e.WorkPackageName,
        Month = e.Month,
        ErrorMessage = e.ErrorMessage,
        Timestamp = e.Timestamp,
        IsResolved = e.IsResolved,
        AuthorId = e.AuthorId ?? "" // 👈 aquí neutralizas el NULL
    })
    .OrderByDescending(e => e.Timestamp)
    .ToListAsync();


        var viewModel = new ProjectManagerViewModel
        {
            Errors = errores
        };

        return View(viewModel);
    }



    [HttpPost]
    public async Task<IActionResult> UploadAndProcessFiles(List<IFormFile> files)
    {
        var viewModel = new ProjectManagerViewModel();

        if (files == null || files.Count == 0)
        {
            ViewBag.Message = "No se seleccionaron archivos.";
            return View("Index", viewModel);
        }

        // Crear carpeta temporal única por sesión de usuario
        var userId = _userManager.GetUserId(User);
        var timestamp = DateTime.Now.ToString("yyyyMMddHHmmssfff");
        var sessionFolder = Path.Combine(Directory.GetCurrentDirectory(), "Uploads", $"{userId}_{timestamp}");

        Directory.CreateDirectory(sessionFolder);

        // Guardar los archivos subidos en la carpeta de sesión
        foreach (var file in files)
        {
            var filePath = Path.Combine(sessionFolder, file.FileName);
            using (var stream = new FileStream(filePath, FileMode.Create))
            {
                await file.CopyToAsync(stream);
            }
        }

        // Procesar solo los archivos subidos en esta sesión
        viewModel = await ProcessFolderForPMs(sessionFolder);

        // Limpiar carpeta tras el procesamiento (éxito o error, no conservamos)
        try
        {
            Directory.Delete(sessionFolder, recursive: true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"No se pudo borrar la carpeta temporal '{sessionFolder}': {ex.Message}");
        }

        ViewBag.Message = "Archivos procesados y esfuerzos ajustados correctamente.";
        return View("Index", viewModel);
    }



    private async Task<ProjectManagerViewModel> ProcessFolderForPMs(string folderPath)
    {
        var viewModel = new ProjectManagerViewModel
        {
            Errors = await _context.TimesheetErrorLogs.OrderByDescending(e => e.Timestamp).ToListAsync()
        };

        var currentUserId = _userManager.GetUserId(User);

        try
        {
            ExcelPackage.LicenseContext = LicenseContext.NonCommercial;

            string[] files;
            if (System.IO.File.Exists(folderPath) && Path.GetExtension(folderPath).Equals(".xlsx", StringComparison.OrdinalIgnoreCase))
            {
                files = new[] { folderPath }; // solo un archivo específico
            }
            else if (System.IO.Directory.Exists(folderPath))
            {
                files = Directory.GetFiles(folderPath, "*.xlsx", SearchOption.AllDirectories);
            }
            else
            {
                throw new FileNotFoundException("No se encontró la ruta especificada ni como archivo ni como carpeta válida.");
            }

            foreach (var file in files)
            {
                using var package = new ExcelPackage(new FileInfo(file));
                var worksheet = package.Workbook.Worksheets[0];

                var monthAbbreviation = worksheet.Cells["B11"].Text.ToLower();
                var year = int.Parse(worksheet.Cells["B12"].Text);
                var month = DateTime.ParseExact(monthAbbreviation, "MMM", CultureInfo.InvariantCulture).Month;
                var daysInMonth = DateTime.DaysInMonth(year, month);
                var personName = worksheet.Cells["B9"].Text.Trim();

                var personnel = await _context.Personnel
                    .FirstOrDefaultAsync(p => (p.Name + " " + p.Surname).ToLower() == personName.ToLower());

                if (personnel == null)
                {
                    await LogErrorIfNotExists(file, personName, "N/A", "N/A", $"{month}/{year}", "Persona no encontrada en la base de datos.", currentUserId);
                    continue;
                }

                var personId = personnel.Id;

                for (int row = 23; row <= worksheet.Dimension.End.Row; row++)
                {
                    var line = worksheet.Cells[row, 1].Text;
                    if (line.StartsWith("Total Hours worked on project", StringComparison.OrdinalIgnoreCase)) break;

                    var lineParts = line.Split(' ');
                    if (lineParts.Length == 0)
                    {
                        await LogErrorIfNotExists(file, personName, "", "", $"{month}/{year}", "Línea vacía o mal formateada.", currentUserId);
                        continue;
                    }

                    string NormalizeStatic(string input) => string.Join(" ", input?.Trim().Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries) ?? Array.Empty<string>());

                    var sapCode = NormalizeStatic(lineParts[0]);

                    var projects = await _context.Projects.ToListAsync();
                    var project = projects.FirstOrDefault(p => NormalizeStatic(p.SapCode) == sapCode);

                    if (project == null)
                    {
                        await LogErrorIfNotExists(file, personName, line, "Desconocido", $"{month}/{year}", "Proyecto no encontrado (SAP Code).", currentUserId);
                        continue;
                    }

                    var wps = await _context.Wps.Where(wp => wp.ProjId == project.ProjId).ToListAsync();

                    var wpMatch = wps.FirstOrDefault(wp => NormalizeStatic(line.ToLower()).Contains(NormalizeStatic(wp.Name.ToLower())));

                    if (wpMatch == null)
                    {
                        await LogErrorIfNotExists(file, personName, line, project.Acronim, $"{month}/{year}", "WP no encontrado en el proyecto.", currentUserId);
                        continue;
                    }

                    var wpxPerson = await _context.Wpxpeople.FirstOrDefaultAsync(wpx => wpx.Person == personId && wpx.Wp == wpMatch.Id);

                    if (wpxPerson == null)
                    {
                        await LogErrorIfNotExists(file, personName, wpMatch.Name, project.Acronim, $"{month}/{year}", "El paquete de trabajo no está vinculado a la persona.", currentUserId);
                        continue;
                    }

                    for (int col = 3; col < 3 + daysInMonth; col++)
                    {
                        var hoursText = worksheet.Cells[row, col].Text;
                        if (string.IsNullOrWhiteSpace(hoursText)) continue;

                        var cultureInfo = new CultureInfo("es-ES");
                        if (!decimal.TryParse(hoursText, NumberStyles.AllowDecimalPoint | NumberStyles.AllowThousands, cultureInfo, out decimal hours))
                        {
                            await LogErrorIfNotExists(file, personName, wpMatch.Name, project.Acronim, $"{month}/{year}", $"Valor no válido en columna {col}: {hoursText}", currentUserId);
                            hours = 0;
                        }

                        var day = col - 2;
                        var date = new DateTime(year, month, day);

                        var existingTimesheet = await _context.Timesheets.FirstOrDefaultAsync(ts => ts.WpxPersonId == wpxPerson.Id && ts.Day == date);
                        if (existingTimesheet != null)
                            existingTimesheet.Hours = hours;
                        else
                            _context.Timesheets.Add(new Timesheet { WpxPersonId = wpxPerson.Id, Day = date, Hours = hours });
                    }

                    await _context.SaveChangesAsync();
                    await _workCalendarService.AdjustEffortAsync(wpMatch.Id, personId, new DateTime(year, month, 1));
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error al procesar la carpeta: {ex.Message}");
        }

        return viewModel;
    }




    [HttpPost]
    public async Task<IActionResult> MarkErrorAsResolved(int id)
    {
        var error = await _context.TimesheetErrorLogs.FindAsync(id);
        if (error != null)
        {
            _context.TimesheetErrorLogs.Remove(error);
            await _context.SaveChangesAsync();
        }

        return RedirectToAction("Index");
    }

    [Authorize(Roles = "ProjectManager,Researcher,Admin")]
    public async Task<IActionResult> ReportError()
    {
        var model = await BuildReportErrorPrefillAsync();
        return View(model);
    }

    private async Task<ReportErrorViewModel> BuildReportErrorPrefillAsync()
    {
        var model = new ReportErrorViewModel
        {
            ReporterUserName = User?.Identity?.Name ?? string.Empty
        };

        var loggedInUser = await _context.Users.FirstOrDefaultAsync(u => u.UserName == User.Identity.Name);
        if (loggedInUser != null)
        {
            var personnel = await _context.Personnel.FirstOrDefaultAsync(p => p.BscId == loggedInUser.UserName);
            if (personnel != null)
            {
                model.ReporterFullName = $"{personnel.Name} {personnel.Surname}".Trim();
                model.ReporterEmail = personnel.Email;
            }

            if (string.IsNullOrWhiteSpace(model.ReporterEmail))
            {
                model.ReporterEmail = loggedInUser.Email;
            }

            if (string.IsNullOrWhiteSpace(model.ReporterFullName))
            {
                model.ReporterFullName = loggedInUser.UserName;
            }
        }

        return model;
    }

    [HttpPost]
    [Authorize(Roles = "ProjectManager,Researcher,Admin")]
    public async Task<IActionResult> SubmitErrorReport(ReportErrorViewModel model)
    {
        if (!ModelState.IsValid)
        {
            var prefillModel = await BuildReportErrorPrefillAsync();
            model.ReporterUserName = prefillModel.ReporterUserName;
            model.ReporterFullName = prefillModel.ReporterFullName;
            model.ReporterEmail = prefillModel.ReporterEmail;
            return View("ReportError", model);
        }

        try
        {
            // Obtener configuración del SMTP desde appsettings.json
            var smtpSettings = _configuration.GetSection("Smtp");
            string host = smtpSettings["Host"];
            int port = int.Parse(smtpSettings["Port"]);
            string username = smtpSettings["Username"];
            string password = smtpSettings["Password"];

            var prefillModel = await BuildReportErrorPrefillAsync();
            var reporterName = string.IsNullOrWhiteSpace(prefillModel.ReporterFullName) ? "Unknown user" : prefillModel.ReporterFullName;
            var reporterUser = string.IsNullOrWhiteSpace(prefillModel.ReporterUserName) ? "n/a" : prefillModel.ReporterUserName;
            var reporterEmail = string.IsNullOrWhiteSpace(prefillModel.ReporterEmail) ? "n/a" : prefillModel.ReporterEmail;

            using (var client = new SmtpClient(host, port))
            {
                client.Credentials = new NetworkCredential(username, password);
                client.EnableSsl = true; // ⚠️ Prueba con false si sigue fallando

                var mailMessage = new MailMessage
                {
                    From = new MailAddress($"{username}@bsc.es"), // Usa el usuario autenticado
                    Subject = $"Nuevo Reporte de Error de {reporterName}: {model.Title}",
                    Body = $"<h3>Detalles del Error</h3>" +
                           $"<p><strong>Usuario:</strong> {reporterUser}</p>" +
                           $"<p><strong>Nombre:</strong> {reporterName}</p>" +
                           $"<p><strong>Correo:</strong> {reporterEmail}</p>" +
                           $"<p><strong>Título:</strong> {model.Title}</p>" +
                           $"<p><strong>Descripción:</strong></p><p>{model.Description}</p>",
                    IsBodyHtml = true
                };

                mailMessage.To.Add("david.reyes@bsc.es");
                mailMessage.CC.Add("cristian.cuadrado@bsc.es");

                // Adjuntar archivo si existe
                if (model.Attachment != null && model.Attachment.Length > 0)
                {
                    var fileName = Path.GetFileName(model.Attachment.FileName);
                    using (var stream = new MemoryStream())
                    {
                        await model.Attachment.CopyToAsync(stream);
                        mailMessage.Attachments.Add(new Attachment(new MemoryStream(stream.ToArray()), fileName));
                    }
                }

                await client.SendMailAsync(mailMessage);
            }

            TempData["SuccessMessage"] = "Your report was sent successfully.";
            return RedirectToAction("ReportError");
        }
        catch (SmtpException smtpEx)
        {
            _logger.LogError($"SMTP Error: {smtpEx.StatusCode} - {smtpEx.Message}");
            TempData["ErrorMessage"] = $"SMTP error: {smtpEx.StatusCode} - {smtpEx.Message}";
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error General al enviar correo: {ex.Message}");
            TempData["ErrorMessage"] = $"There was an error sending the report: {ex.Message}";
        }

        return RedirectToAction("ReportError");
    }

    private async Task LogErrorIfNotExists(string filePath, string personName, string workPackageName, string projectName, string month, string errorMessage, string authorId)
    {
        string fileName = Path.GetFileName(filePath);

        bool exists = await _context.TimesheetErrorLogs.AnyAsync(e =>
            e.FileName == fileName &&
            e.PersonName == personName &&
            e.WorkPackageName == workPackageName &&
            e.ProjectName == projectName &&
            e.Month == month &&
            e.ErrorMessage == errorMessage
        );

        if (!exists)
        {
            _context.TimesheetErrorLogs.Add(new TimesheetErrorLog
            {
                FileName = fileName,
                PersonName = personName,
                WorkPackageName = workPackageName,
                ProjectName = projectName,
                Month = month,
                ErrorMessage = errorMessage,
                AuthorId = authorId
            });
            await _context.SaveChangesAsync();
        }
    }


}

