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

[Authorize(Roles = "ProjectManager")]
public class ProjectManagerController : Controller
{
    private readonly WorkCalendarService _workCalendarService;
    private readonly ILogger<ProjectManagerController> _logger;
    private readonly TRSDBContext _context;
    private readonly IConfiguration _configuration;

    public ProjectManagerController(WorkCalendarService workCalendarService, ILogger<ProjectManagerController> logger, TRSDBContext context, IConfiguration configuration)
    {
        _workCalendarService = workCalendarService;
        _logger = logger;
        _context = context;
        _configuration = configuration;
    }

    public async Task<IActionResult> Index()
    {
        var viewModel = new ProjectManagerViewModel
        {
            Errors = await _context.TimesheetErrorLogs.OrderByDescending(e => e.Timestamp).ToListAsync()
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
            return View("Index", viewModel); // Se pasa el modelo vacío para evitar NullReferenceException
        }

        string uploadPath = Path.Combine(Directory.GetCurrentDirectory(), "Uploads");
        if (!Directory.Exists(uploadPath))
        {
            Directory.CreateDirectory(uploadPath);
        }

        foreach (var file in files)
        {
            var filePath = Path.Combine(uploadPath, file.FileName);
            using (var stream = new FileStream(filePath, FileMode.Create))
            {
                await file.CopyToAsync(stream);
            }
        }

        // Ejecutamos el proceso combinado
        viewModel = await ProcessFolderForPMs(uploadPath);

        ViewBag.Message = "Archivos procesados y esfuerzos ajustados correctamente.";
        return View("Index", viewModel);
    }


    private async Task<ProjectManagerViewModel> ProcessFolderForPMs(string folderPath)
    {
        var viewModel = new ProjectManagerViewModel
        {
            Errors = await _context.TimesheetErrorLogs.OrderByDescending(e => e.Timestamp).ToListAsync() // Ahora solo leemos desde la BD
        };

        try
        {
            // Configurar el contexto de la licencia de EPPlus
            ExcelPackage.LicenseContext = LicenseContext.NonCommercial;
            var files = Directory.GetFiles(folderPath, "*.xlsx", SearchOption.AllDirectories);

            foreach (var file in files)
            {
                using (var package = new ExcelPackage(new FileInfo(file)))
                {
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
                        var error = new TimesheetErrorLog
                        {
                            FileName = Path.GetFileName(file),
                            PersonName = personName,
                            WorkPackageName = "N/A",
                            ProjectName = "N/A",
                            Month = $"{month}/{year}",
                            ErrorMessage = "Persona no encontrada en la base de datos."
                        };

                        _context.TimesheetErrorLogs.Add(error);
                        await _context.SaveChangesAsync();
                        continue;
                    }

                    var personId = personnel.Id;

                    for (int row = 23; row <= worksheet.Dimension.End.Row; row++)
                    {
                        var line = worksheet.Cells[row, 1].Text;

                        // Condición de corte si llegamos al final de los paquetes de trabajo
                        if (line.StartsWith("Total Hours worked on project", StringComparison.OrdinalIgnoreCase))
                        {
                            break;
                        }

                        var queryResult = _context.Projects
                            .Join(_context.Wps,
                                  p => p.ProjId,
                                  wp => wp.ProjId,
                                  (p, wp) => new
                                  {
                                      FullString = (p.SapCode + " " + p.Acronim + " - " + wp.Name + " " + wp.Title).ToLower(),
                                      Project = p,
                                      WorkPackage = wp
                                  })
                            .FirstOrDefault(result => result.FullString == line.ToLower());

                        if (queryResult == null)
                        {
                            var possiblePackage = line.Split('-').LastOrDefault()?.Trim();

                            if (!string.IsNullOrEmpty(possiblePackage))
                            {
                                var packageQuery = _context.Projects
                                    .Join(_context.Wps,
                                          p => p.ProjId,
                                          wp => wp.ProjId,
                                          (p, wp) => new
                                          {
                                              PackageName = wp.Name.ToLower(),
                                              Project = p,
                                              WorkPackage = wp
                                          })
                                    .FirstOrDefault(result => result.PackageName == possiblePackage.ToLower());

                                if (packageQuery != null)
                                {
                                    queryResult = new
                                    {
                                        FullString = "",
                                        Project = packageQuery.Project,
                                        WorkPackage = packageQuery.WorkPackage
                                    };
                                }
                            }
                        }

                        if (queryResult == null)
                        {
                            var error = new TimesheetErrorLog
                            {
                                FileName = Path.GetFileName(file),
                                PersonName = personName,
                                WorkPackageName = line,
                                ProjectName = "Desconocido",
                                Month = $"{month}/{year}",
                                ErrorMessage = "Paquete de trabajo no encontrado."
                            };

                            _context.TimesheetErrorLogs.Add(error);
                            await _context.SaveChangesAsync();
                            continue;
                        }

                        var project = queryResult.Project;
                        var workPackage = queryResult.WorkPackage;

                        var wpxPerson = await _context.Wpxpeople.FirstOrDefaultAsync(wpx => wpx.Person == personId && wpx.Wp == workPackage.Id);
                        if (wpxPerson == null)
                        {
                            var error = new TimesheetErrorLog
                            {
                                FileName = Path.GetFileName(file),
                                PersonName = personName,
                                WorkPackageName = workPackage.Name,
                                ProjectName = project.Acronim,
                                Month = $"{month}/{year}",
                                ErrorMessage = "El paquete de trabajo no está vinculado a la persona."
                            };

                            _context.TimesheetErrorLogs.Add(error);
                            await _context.SaveChangesAsync();
                            continue;
                        }

                        decimal totalHours = 0;

                        for (int col = 3; col < 3 + daysInMonth; col++)
                        {
                            var hoursText = worksheet.Cells[row, col].Text;
                            if (string.IsNullOrWhiteSpace(hoursText)) continue;

                            var cultureInfo = new CultureInfo("es-ES");
                            if (!decimal.TryParse(hoursText, NumberStyles.AllowDecimalPoint | NumberStyles.AllowThousands, cultureInfo, out decimal hours))
                            {
                                var error = new TimesheetErrorLog
                                {
                                    FileName = Path.GetFileName(file),
                                    PersonName = personName,
                                    WorkPackageName = workPackage.Name,
                                    ProjectName = project.Acronim,
                                    Month = $"{month}/{year}",
                                    ErrorMessage = $"Valor no válido en columna {col}: {hoursText}"
                                };

                                _context.TimesheetErrorLogs.Add(error);
                                await _context.SaveChangesAsync();                               

                                hours = 0;
                            }

                            var day = col - 2;
                            var date = new DateTime(year, month, day);

                            var existingTimesheet = await _context.Timesheets
                                .FirstOrDefaultAsync(ts => ts.WpxPersonId == wpxPerson.Id && ts.Day == date);

                            if (existingTimesheet != null)
                            {
                                existingTimesheet.Hours = hours;
                            }
                            else
                            {
                                var timesheet = new Timesheet
                                {
                                    WpxPersonId = wpxPerson.Id,
                                    Day = date,
                                    Hours = hours
                                };
                                _context.Timesheets.Add(timesheet);
                            }

                            totalHours += hours;
                        }

                        await _context.SaveChangesAsync();
                        await _workCalendarService.AdjustEffortAsync(workPackage.Id, personId, new DateTime(year, month, 1));
                    }
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

    public IActionResult ReportError()
    {
        return View();
    }

    [HttpPost]    
    public async Task<IActionResult> SubmitErrorReport(ReportErrorViewModel model)
    {
        if (!ModelState.IsValid)
        {
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

            // Obtener el usuario logueado
            var loggedInUser = await _context.Users.FirstOrDefaultAsync(u => u.UserName == User.Identity.Name);


            // Inicializar variables para nombre y apellido
            string name = string.Empty;
            string surname = string.Empty;

            if (loggedInUser != null)
            {
                // Obtener la información de Personnel usando el BscId
                var personnel = await _context.Personnel.FirstOrDefaultAsync(p => p.BscId == loggedInUser.UserName);
                if (personnel != null)
                {
                    name = personnel.Name;
                    surname = personnel.Surname;
                }
            }

            using (var client = new SmtpClient(host, port))
            {
                client.Credentials = new NetworkCredential(username, password);
                client.EnableSsl = true; // ⚠️ Prueba con false si sigue fallando

                var mailMessage = new MailMessage
                {
                    From = new MailAddress($"{username}@bsc.es"), // Usa el usuario autenticado
                    Subject = $"Nuevo Reporte de Error de {name} {surname}: {model.Title}",
                    Body = $"<h3>Detalles del Error</h3>" +
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

            TempData["SuccessMessage"] = "El reporte ha sido enviado con éxito.";
            return RedirectToAction("ReportError");
        }
        catch (SmtpException smtpEx)
        {
            _logger.LogError($"SMTP Error: {smtpEx.StatusCode} - {smtpEx.Message}");
            TempData["ErrorMessage"] = $"Hubo un error SMTP: {smtpEx.StatusCode} - {smtpEx.Message}";
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error General al enviar correo: {ex.Message}");
            TempData["ErrorMessage"] = $"Hubo un error al enviar el reporte: {ex.Message}";
        }

        return RedirectToAction("ReportError");
    }


}

