using System;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using TRS2._0.Models.DataModels;
using Newtonsoft.Json.Linq;
using TRS2._0.Models.ViewModels;
using static TRS2._0.Models.ViewModels.PersonnelEffortPlanViewModel;
using System.Diagnostics;
namespace TRS2._0.Services;

public class WorkCalendarService
{
        private readonly TRSDBContext _context;

        public WorkCalendarService(TRSDBContext context)
        {
            _context = context;
        }

        public async Task<int> CalculateWorkingDays(int year, int month)
        {
            // Obtiene el número de días en el mes
            int daysInMonth = DateTime.DaysInMonth(year, month);
            int workingDays = 0;

            for (int day = 1; day <= daysInMonth; day++)
            {
                DateTime currentDate = new DateTime(year, month, day);

                // Comprueba si el día actual no es ni sábado ni domingo
                if (currentDate.DayOfWeek != DayOfWeek.Saturday && currentDate.DayOfWeek != DayOfWeek.Sunday)
                {
                    // Comprueba si el día actual no es un día festivo
                    bool isHoliday = await _context.NationalHolidays
                        .AnyAsync(h => h.Date == currentDate);

                    if (!isHoliday)
                    {
                        workingDays++;
                    }
                }
            }

            return workingDays;
        }

    public async Task<decimal> CalculateDailyPM(int personId, DateTime date)
    {
        // Comprobar si hay una ausencia registrada en la tabla 'Leave'
        bool isOnLeave = await _context.Leaves.AnyAsync(l => l.PersonId == personId && l.Day == date);
        if (isOnLeave)
        {
            return 0; // PM es 0 si la persona está ausente
        }

        // Obtener el valor de PM del día
        var dailyPmValue = await _context.DailyPMValues
            .Where(d => d.Year == date.Year && d.Month == date.Month)
            .Select(d => d.PmPerDay)
            .FirstOrDefaultAsync();

        // Obtener la dedicación aplicable para la fecha
        var applicableDedication = await _context.Dedications
            .Where(d => d.PersId == personId && d.Start <= date && d.End >= date)
            .OrderByDescending(d => d.Type) // Priorizar por el 'Type' más alto
            .Select(d => d.Reduc)
            .FirstOrDefaultAsync();

        return dailyPmValue * (1 - applicableDedication); // Aplicar reducción
    }

    public async Task<decimal> CalculateMonthlyPM(int personId, int year, int month)
    {
        // Obtener todos los contratos (dedicaciones) para el mes y año especificados
        var dedications = await _context.Dedications
            .Where(d => d.PersId == personId &&
                        d.Start <= new DateTime(year, month, DateTime.DaysInMonth(year, month)) &&
                        d.End >= new DateTime(year, month, 1))
            .OrderBy(d => d.Start)
            .ToListAsync();

        // Si no hay dedicaciones que cubran el mes, retornar PM como 0
        if (!dedications.Any())
        {
            return 0;
        }

        // Obtener los días festivos para el mes y año especificados
         var holidays = await _context.NationalHolidays
            .Where(h => h.Date.Year == year && h.Date.Month == month)
            .Select(h => h.Date)
            .ToListAsync();

        // Obtener los días de ausencia para el mes y año especificados
        var leaveDays = await _context.Leaves
            .Where(l => l.PersonId == personId && l.Day.Year == year && l.Day.Month == month)
            .Select(l => l.Day)
            .ToListAsync();

        decimal totalPm = 0;

        // Valor de PM por día obtenido de la tabla DailyPMValues
        var dailyPmValue = await _context.DailyPMValues
            .Where(d => d.Year == year && d.Month == month)
            .Select(d => d.PmPerDay)
            .FirstOrDefaultAsync();

        // Procesar cada día del mes
        for (DateTime currentDate = new DateTime(year, month, 1); currentDate <= new DateTime(year, month, DateTime.DaysInMonth(year, month)); currentDate = currentDate.AddDays(1))
        {
            // Omitir sábados, domingos, días festivos y días de ausencia
            if (currentDate.DayOfWeek == DayOfWeek.Saturday ||
                currentDate.DayOfWeek == DayOfWeek.Sunday ||
                holidays.Contains(currentDate) ||
                leaveDays.Contains(currentDate))
            {
                continue;
            }

            // Aplicar la reducción de dedicación más actual para la fecha
            var applicableDedication = dedications
                .Where(d => currentDate >= d.Start && currentDate <= d.End)
                .OrderByDescending(d => d.Type) // Dar prioridad a la dedicación con el 'Type' más alto
                .Select(d => d.Reduc)
                .FirstOrDefault();

            // Calcular PM para el día y acumular al total
            totalPm += dailyPmValue * (1 - applicableDedication);
        }

        return Math.Round(totalPm, 2);
    }

    public async Task<decimal> CalculateMonthlyEffortForPerson(int personId, int year, int month)
    {
        // Obtener todos los esfuerzos asociados a la persona que están dentro del rango de fechas especificado
        var efforts = await _context.Persefforts
            .Include(pe => pe.WpxPersonNavigation)
            .Where(pe => pe.WpxPersonNavigation.Person == personId &&
                         pe.Month.Year == year &&
                         pe.Month.Month == month)
            .ToListAsync();

        // Sumar los valores de esfuerzo para el mes dado
        decimal totalEffort = efforts.Sum(e => e.Value);

        return Math.Round(totalEffort, 2);
    }

    public async Task<Dictionary<DateTime, decimal>> CalculateMonthlyEffortForPersonInProject(int personId, DateTime startDate, DateTime endDate, int projectId)
    {
        // Asegurarse de que la fecha de fin sea al menos el último momento del mes indicado
        endDate = new DateTime(endDate.Year, endDate.Month, DateTime.DaysInMonth(endDate.Year, endDate.Month));

        // Obtener todos los esfuerzos asociados a la persona dentro del rango de fechas y proyecto especificado
        var efforts = await _context.Persefforts
            .Include(pe => pe.WpxPersonNavigation)
            .Where(pe => pe.WpxPersonNavigation.Person == personId &&
                         pe.WpxPersonNavigation.WpNavigation.ProjId == projectId &&
                         pe.Month >= startDate && pe.Month <= endDate)
            .ToListAsync();

        // Agrupar los esfuerzos por mes y sumarlos
        var monthlyEfforts = efforts
            .GroupBy(pe => new DateTime(pe.Month.Year, pe.Month.Month, 1))
            .Select(g => new { Month = g.Key, TotalEffort = g.Sum(e => e.Value) })
            .ToDictionary(g => g.Month, g => Math.Round(g.TotalEffort, 2));

        return monthlyEfforts;
    }



    public async Task<Dictionary<DateTime, decimal>> GetDeclaredHoursPerMonthForPerson(int personId, DateTime startDate, DateTime endDate)
    {
        // Asegúrate de que endDate es el último momento del mes para incluir todos los días del mes
        endDate = new DateTime(endDate.Year, endDate.Month, DateTime.DaysInMonth(endDate.Year, endDate.Month));

        var hoursPerMonth = await _context.Timesheets
            .Where(ts => ts.WpxPersonNavigation.Person == personId &&
                         ts.Day >= startDate &&
                         ts.Day <= endDate)
            .GroupBy(ts => new
            {
                Year = ts.Day.Year,
                Month = ts.Day.Month
            })
            .Select(g => new
            {
                Month = new DateTime(g.Key.Year, g.Key.Month, 1), // Usa el primer día del mes como clave
                TotalHours = g.Sum(ts => ts.Hours) // Suma las horas declaradas
            })
            .ToDictionaryAsync(g => g.Month, g => g.TotalHours);

        return hoursPerMonth;
    }


    public async Task<Dictionary<DateTime, decimal>> CalculateTotalHoursForPerson(int personId, DateTime startDate, DateTime endDate, int projectId, Dictionary<DateTime, int> workingDaysPerMonth)
    {
        var totalExecutionStopwatch = Stopwatch.StartNew();

        var totalHoursPerMonth = new Dictionary<DateTime, decimal>();

        var affiliationsStopwatch = Stopwatch.StartNew();
        var affiliations = await _context.AffxPersons
            .Where(a => a.PersonId == personId && !(a.End < startDate || a.Start > endDate))
            .Include(a => a.Affiliation)
            .ToListAsync();
        affiliationsStopwatch.Stop();
        Console.WriteLine($"Affiliations fetch took: {affiliationsStopwatch.ElapsedMilliseconds} ms");

        var affHoursStopwatch = Stopwatch.StartNew();
        var affHoursList = await _context.AffHours
            .Where(ah => affiliations.Select(a => a.AffId).Contains(ah.AffId) &&
                         ah.EndDate >= startDate &&
                         ah.StartDate <= endDate)
            .ToListAsync();
        affHoursStopwatch.Stop();
        Console.WriteLine($"AffHours fetch took: {affHoursStopwatch.ElapsedMilliseconds} ms");

        startDate = new DateTime(startDate.Year, startDate.Month, 1);
        endDate = new DateTime(endDate.Year, endDate.Month, DateTime.DaysInMonth(endDate.Year, endDate.Month));

        var monthlyCalculationStopwatch = new Stopwatch();

        while (startDate <= endDate)
        {
            decimal hoursForMonth = 0;
            if (workingDaysPerMonth.TryGetValue(new DateTime(startDate.Year, startDate.Month, 1), out int workingDays))
            {
                // Aquí utilizas workingDays obtenidos de DailyPmValues
                foreach (var affiliation in affiliations)
                {
                    var affHours = affHoursList.FirstOrDefault(ah => ah.AffId == affiliation.AffId && ah.StartDate <= startDate && ah.EndDate >= startDate)?.Hours ?? 0;
                    hoursForMonth += affHours * workingDays;
                }

                if (affiliations.Any())
                {
                    hoursForMonth /= affiliations.Count;
                }

                totalHoursPerMonth.Add(new DateTime(startDate.Year, startDate.Month, 1), hoursForMonth);
            }

            startDate = startDate.AddMonths(1);
        }

        totalExecutionStopwatch.Stop();
        Console.WriteLine($"Total execution time of CalculateTotalHoursForPerson: {totalExecutionStopwatch.ElapsedMilliseconds} ms");

        return totalHoursPerMonth;
    }


    public async Task<bool> IsOutOfContract(int personId, int year, int month)
    {
        var startDate = new DateTime(year, month, 1);
        var endDate = startDate.AddMonths(1).AddDays(-1);

        // Comprobamos si existe algún registro en la tabla 'Dedication' que coincida con el mes y año dado,
        // donde el inicio del contrato sea anterior o igual al final del mes y el fin del contrato sea posterior o igual al inicio del mes.
        var isOutOfContract = !await _context.Dedications.AnyAsync(d => d.PersId == personId &&
                                                                    d.Start <= endDate &&
                                                                    d.End >= startDate);

        return isOutOfContract;
    }

    public async Task<bool> IsOverloaded(decimal monthlyEffort, decimal monthlyPm)
    {
        return monthlyEffort > monthlyPm;
    }

    public async Task<List<Leave>> GetLeavesForPerson(int personId, int year, int month)
    {
        var startDate = new DateTime(year, month, 1);
        var endDate = startDate.AddMonths(1).AddDays(-1);

        // Realiza la consulta a la base de datos para obtener las ausencias que ocurren dentro del mes dado.
        var leaves = await _context.Leaves
                            .Where(l => l.PersonId == personId
                                        && l.Day >= startDate
                                        && l.Day <= endDate
                                        )
                            .ToListAsync();

        return leaves;
    }

    public async Task<List<MonthStatus>> CalculateMonthlyStatusesForPerson(int personId, List<DateTime> uniqueMonths, Dictionary<string, decimal> totalEffortsPerMonth, Dictionary<string, decimal> pmValuesPerMonth)
    {
        var monthStatuses = new List<MonthStatus>();

        foreach (var month in uniqueMonths)
        {
            int status = 0;
            List<int> additionalStatuses = new List<int>();
            string gradient = "";

            bool isOutOfContract = await IsOutOfContract(personId, month.Year, month.Month);
            if (isOutOfContract)
            {
                status = 1; // OutOfContract
            }
            else
            {
                string monthKey = $"{month.Year}-{month.Month:D2}";
                if (totalEffortsPerMonth.ContainsKey(monthKey) && pmValuesPerMonth.ContainsKey(monthKey))
                {
                    bool isOverloaded = totalEffortsPerMonth[monthKey] > pmValuesPerMonth[monthKey];
                    if (isOverloaded)
                    {
                        status = 2; // Overloaded
                    }
                }

                if (status == 0)
                {
                    var leaves = await GetLeavesForPerson(personId, month.Year, month.Month);
                    foreach (var leave in leaves)
                    {
                        switch (leave.Type)
                        {
                            case 1: // Leave
                                additionalStatuses.Add(3);
                                gradient += "lightsalmon, ";
                                break;
                            case 2: // Personal Holiday
                                additionalStatuses.Add(4);
                                gradient += "lightblue, ";
                                break;
                            case 3: // No Contract Period
                                additionalStatuses.Add(5);
                                gradient += "purple, ";
                                break;
                        }
                    }
                    if (gradient.Length > 0)
                    {
                        gradient = $"linear-gradient({gradient.TrimEnd(',', ' ')})";
                        status = 3; // Setting status to 3 for leaves as default if not already set.
                    }
                }
            }

            monthStatuses.Add(new MonthStatus
            {
                Month = month,
                Status = status,
                AdditionalStatuses = additionalStatuses,
                Gradient = gradient
            });
        }

        return monthStatuses;
    }

    public async Task<List<MonthStatus>> CalculateMonthlyStatusesForPersonWithLists(
    int personId,
    List<DateTime> uniqueMonths,
    Dictionary<string, decimal> totalEffortsPerMonth,
    Dictionary<string, decimal> pmValuesPerMonth,
    List<ProjectMonthLock> projectMonthLocks)
    {
        var monthStatuses = new List<MonthStatus>();
        var outOfContractForMonths = await IsOutOfContractForMonths(personId, uniqueMonths);
        var leavesForMonths = await GetLeavesForMonths(personId, uniqueMonths);
        var travelDetailsForMonths = await GetTravelDatesForMonths(personId, uniqueMonths);

        foreach (var month in uniqueMonths)
        {
            int status = 0;
            var additionalStatuses = new List<int>();
            var gradientParts = new List<string>();
            var monthKey = $"{month.Year}-{month.Month:D2}";
            var travelDetails = travelDetailsForMonths.GetValueOrDefault(month, new List<TravelDetails>());
            bool isLocked = projectMonthLocks.Any(l => l.Year == month.Year && l.Month == month.Month && l.IsLocked);

            // Determine if out of contract
            if (outOfContractForMonths.TryGetValue(month, out bool isOutOfContract) && isOutOfContract)
            {
                status = 1; // OutOfContract
            }
            else if (totalEffortsPerMonth.TryGetValue(monthKey, out decimal totalEffort) &&
                     pmValuesPerMonth.TryGetValue(monthKey, out decimal pmValue) &&
                     totalEffort > pmValue)
            {
                status = 2; // Overloaded
            }

            // Process leaves if not out of contract or overloaded
            if (status == 0 && leavesForMonths.TryGetValue(month, out var leaves))
            {
                foreach (var leave in leaves)
                {
                    switch (leave.Type)
                    {
                        case 1: // Leave
                            additionalStatuses.Add(3);
                            gradientParts.Add("lightsalmon");
                            break;
                        case 2: // Personal Holiday
                            additionalStatuses.Add(4);
                            gradientParts.Add("lightblue");
                            break;
                        case 3: // No Contract Period
                            additionalStatuses.Add(5);
                            gradientParts.Add("purple");
                            break;
                    }
                }
            }

            // Check for travels in the current month
            if (travelDetails.Any())
            {
                status = 4; // Estado para indicar que hay viajes
                
            }
            if (gradientParts.Any())
            {
                var gradient = $"linear-gradient(to right, {string.Join(", ", gradientParts)})";
                // Set status to 3 for leaves if there are any leaves
                status = 3;
            }

            var uniqueProjIds = travelDetails.Select(td => td.ProjId).Distinct().ToList();

            monthStatuses.Add(new MonthStatus
            {
                Month = month,
                Status = status,
                IsLocked = isLocked,
                AdditionalStatuses = additionalStatuses,
                Gradient = gradientParts.Any() ? $"linear-gradient(to right, {string.Join(", ", gradientParts)})" : "",
                TravelDetails = travelDetails,
                UniqueProjIds = uniqueProjIds // Lista de projId únicos para los viajes de ese mes
            });
        }

        return monthStatuses;
    }


    public async Task<Dictionary<DateTime, bool>> IsOutOfContractForMonths(int personId, List<DateTime> months)
    {
        var startDates = months.Select(m => new DateTime(m.Year, m.Month, 1));
        var endDates = months.Select(m => new DateTime(m.Year, m.Month, DateTime.DaysInMonth(m.Year, m.Month)));

        var contracts = await _context.Dedications
            .Where(d => d.PersId == personId &&
                        d.End >= startDates.Min() &&
                        d.Start <= endDates.Max())
            .ToListAsync();

        var outOfContract = new Dictionary<DateTime, bool>();
        foreach (var month in months)
        {
            var startDate = new DateTime(month.Year, month.Month, 1);
            var endDate = startDate.AddMonths(1).AddDays(-1);
            // Verificar si existe algún contrato que cubra el mes actual
            outOfContract[month] = !contracts.Any(d => d.Start <= endDate && d.End >= startDate);
        }

        return outOfContract;
    }

    public async Task<Dictionary<DateTime, List<Leave>>> GetLeavesForMonths(int personId, List<DateTime> months)
    {
        var startDate = months.Min(m => new DateTime(m.Year, m.Month, 1));
        var endDate = months.Max(m => new DateTime(m.Year, m.Month, DateTime.DaysInMonth(m.Year, m.Month)));

        var leaves = await _context.Leaves
            .Where(l => l.PersonId == personId && l.Day >= startDate && l.Day <= endDate)
            .ToListAsync();

        var leavesByMonth = new Dictionary<DateTime, List<Leave>>();
        foreach (var month in months)
        {
            leavesByMonth[month] = leaves.Where(l => l.Day.Year == month.Year && l.Day.Month == month.Month).ToList();
        }

        return leavesByMonth;
    }

    

    public async Task<Dictionary<DateTime, List<TravelDetails>>> GetTravelDatesForMonths(int personId, List<DateTime> uniqueMonths)
    {
        var travelDatesForMonths = new Dictionary<DateTime, List<TravelDetails>>();

        // Paso 1: Recuperar todos los viajes para la persona sin filtrar por 'uniqueMonths' directamente en la consulta.
        var allTravels = await _context.liqdayxproject
            .Where(ldp => ldp.PersId == personId)
            .Select(ldp => new TravelDetails { Day = ldp.Day, ProjId = ldp.ProjId })
            .ToListAsync();

        // Paso 2: Filtrar los viajes en memoria para asociarlos con los meses correspondientes.
        foreach (var month in uniqueMonths)
        {
            var firstDayOfMonth = new DateTime(month.Year, month.Month, 1);
            var lastDayOfMonth = firstDayOfMonth.AddMonths(1).AddDays(-1);

            var travelsInMonth = allTravels
                .Where(td => td.Day >= firstDayOfMonth && td.Day <= lastDayOfMonth)
                .ToList();

            travelDatesForMonths.Add(month, travelsInMonth);
        }

        return travelDatesForMonths;
    }

    public async Task<List<DateTime>> GetDaysForPerson(int personId, int year, int month)
    {
        var startDate = new DateTime(year, month, 1);
        var endDate = startDate.AddMonths(1).AddDays(-1);

        var days = await _context.Timesheets
            .Where(ts => ts.WpxPersonNavigation.Person == personId && ts.Day >= startDate && ts.Day <= endDate)
            .Select(ts => ts.Day)
            .Distinct()
            .ToListAsync();

        return days;
    }

    public async Task<List<string>> GetFormattedDaysOfMonthAsync(int year, int month)
    {
        // Lista para almacenar los días formateados
        List<string> formattedDays = new List<string>();

        // Calcula el número de días en el mes especificado
        int daysInMonth = DateTime.DaysInMonth(year, month);

        // Recorre todos los días del mes
        for (int day = 1; day <= daysInMonth; day++)
        {
            // Simula una operación asíncrona, por ejemplo, esperando una tarea completada
            await Task.CompletedTask;

            // Crea una fecha con el año, mes y día actual del bucle
            DateTime date = new DateTime(year, month, day);

            // Formatea el día y el nombre del día en inglés
            string formattedDay = $"{date:dd} {date:dddd}";

            // Agrega el día formateado a la lista
            formattedDays.Add(formattedDay);
        }

        return formattedDays;
    }

    public async Task<List<TravelDetails>> GetTravelsForThisMonth(int personId, int year, int month)
    {
        var startDate = new DateTime(year, month, 1);
        var endDate = startDate.AddMonths(1).AddDays(-1);

        var travels = await _context.liqdayxproject
            .Where(ldp => ldp.PersId == personId && ldp.Day >= startDate && ldp.Day <= endDate)
            .GroupBy(ldp => new { ldp.LiqId, ldp.ProjId })
            .Select(g => new
            {
                LiqId = g.Key.LiqId,
                ProjId = g.Key.ProjId,
                Dedication = g.Average(ldp => ldp.Dedication), 
                StartDate = g.Min(ldp => ldp.Day),
                EndDate = g.Max(ldp => ldp.Day),
                Project = g.Select(ldp => ldp.Project).FirstOrDefault() // Asume que todos los registros tienen el mismo proyecto
            })
            .ToListAsync();

        var travelDetailsList = travels.Select(t => new TravelDetails
        {
            LiqId = t.LiqId,
            ProjId = t.ProjId,
            Dedication = t.Dedication,
            StartDate = t.StartDate,
            EndDate = t.EndDate,
            ProjectAcronimo = t.Project.Acronim,
            ProjectSAPCode = t.Project.SapCode
        }).ToList();

        return travelDetailsList;
    }



    public async Task<Dictionary<DateTime, decimal>> CalculateDailyWorkHours(int personId, int year, int month)
    {
        var dailyWorkHours = new Dictionary<DateTime, decimal>();
        DateTime startDate = new DateTime(year, month, 1);
        int daysInMonth = DateTime.DaysInMonth(year, month);
        decimal monthlyDedication = await CalculateMonthlyPM(personId, year, month); 

        // Obtener todas las afiliaciones para la persona en el mes dado
        var affiliations = await _context.AffxPersons
                            .Where(ap => ap.PersonId == personId && ap.Start <= startDate.AddMonths(1).AddDays(-1) && ap.End >= startDate)
                            .Select(ap => ap.AffId)
                            .Distinct()
                            .ToListAsync();

        // Obtener las horas de trabajo de todas las afiliaciones aplicables
        var affHoursList = await _context.AffHours
                            .Where(ah => affiliations.Contains(ah.AffId) && ah.StartDate <= startDate.AddMonths(1).AddDays(-1) && ah.EndDate >= startDate)
                            .ToListAsync();

        for (int day = 1; day <= daysInMonth; day++)
        {
            DateTime currentDate = new DateTime(year, month, day);

            // Ignora sábados, domingos y festivos
            if (currentDate.DayOfWeek == DayOfWeek.Saturday || currentDate.DayOfWeek == DayOfWeek.Sunday || await IsHoliday(currentDate))
            {
                continue;
            }

            // Encuentra las horas de afiliación aplicables para el día actual
            var currentHours = affHoursList.FirstOrDefault(ah => currentDate >= ah.StartDate && currentDate <= ah.EndDate)?.Hours ?? 0;

            // Ajusta las horas diarias por la dedicación mensual
            decimal adjustedDailyHours = currentHours * monthlyDedication;

            dailyWorkHours.Add(currentDate, adjustedDailyHours);
        }

        return dailyWorkHours;
    }

    public async Task<Dictionary<DateTime, decimal>> CalculateDailyWorkHoursWithDedication(int personId, int year, int month)
    {
        var dailyWorkHours = new Dictionary<DateTime, decimal>();
        DateTime startDate = new DateTime(year, month, 1);
        int daysInMonth = DateTime.DaysInMonth(year, month);

        // Obtener todas las afiliaciones y dedicaciones para la persona en el mes dado
        var affiliations = await _context.AffxPersons
                            .Include(ap => ap.Affiliation) // Asegúrate de incluir las entidades relacionadas necesarias
                            .Where(ap => ap.PersonId == personId && ap.Start <= startDate.AddMonths(1).AddDays(-1) && ap.End >= startDate)
                            .ToListAsync();

        // Obtener las dedicaciones del mes para la persona
        var dedications = await _context.Dedications
                            .Where(d => d.PersId == personId && d.Start <= startDate.AddMonths(1).AddDays(-1) && d.End >= startDate)
                            .ToListAsync();

        for (int day = 1; day <= daysInMonth; day++)
        {
            DateTime currentDate = new DateTime(year, month, day);

            // Ignora sábados, domingos y festivos
            if (currentDate.DayOfWeek == DayOfWeek.Saturday || currentDate.DayOfWeek == DayOfWeek.Sunday || await IsHoliday(currentDate))
            {
                continue;
            }

            // Encuentra las horas de afiliación y dedicación aplicables para el día actual
            var currentAffiliationHours = affiliations
                .Where(ap => currentDate >= ap.Start && currentDate <= ap.End)
                .SelectMany(ap => _context.AffHours.Where(ah => ah.AffId == ap.AffId && currentDate >= ah.StartDate && currentDate <= ah.EndDate))
                .FirstOrDefault()?.Hours ?? 0;

            var currentDedication = dedications
                .Where(d => currentDate >= d.Start && currentDate <= d.End)
                .FirstOrDefault()?.Reduc;

            // Si no se encuentra dedicación específica o Reduc es 0.00, asume una jornada completa
            if (!currentDedication.HasValue || currentDedication.Value == 0.00M)
            {
                
            }

            decimal adjustedDailyHours = currentAffiliationHours * (1 - currentDedication.Value);

            dailyWorkHours.Add(currentDate, adjustedDailyHours);
        }

        return dailyWorkHours;
    }

    public async Task <List<DateTime>> GetHolidaysForMonth(int year, int month)
    {
        var holidays = await _context.NationalHolidays
            .Where(h => h.Date.Year == year && h.Date.Month == month)
            .Select(h => h.Date)
            .ToListAsync();

        return holidays;
    }

    // Función auxiliar para verificar si un día es festivo
    private async Task<bool> IsHoliday(DateTime date)
    {
        return await _context.NationalHolidays.AnyAsync(h => h.Date == date);
    }


    public async Task<decimal> CalculateAdjustedMonthlyPM(int personId, int year, int month, DateTime startDate, DateTime endDate)
    {
        // Asegurar que las fechas de inicio y fin están dentro del mes y año especificados
        startDate = new DateTime(year, month, 1) > startDate ? new DateTime(year, month, 1) : startDate;
        endDate = new DateTime(year, month, DateTime.DaysInMonth(year, month)) < endDate ? new DateTime(year, month, DateTime.DaysInMonth(year, month)) : endDate;

        var dedications = await _context.Dedications
            .Where(d => d.PersId == personId &&
                        d.Start <= endDate &&
                        d.End >= startDate)
            .OrderBy(d => d.Start)
            .ToListAsync();

        if (!dedications.Any())
        {
            return 0;
        }

        var holidays = await _context.NationalHolidays
            .Where(h => h.Date >= startDate && h.Date <= endDate)
            .Select(h => h.Date)
            .ToListAsync();

        var leaveDays = await _context.Leaves
            .Where(l => l.PersonId == personId && l.Day >= startDate && l.Day <= endDate)
            .Select(l => l.Day)
            .ToListAsync();

        decimal totalPm = 0;

        var dailyPmValue = await _context.DailyPMValues
            .Where(d => d.Year == year && d.Month == month)
            .Select(d => d.PmPerDay)
            .FirstOrDefaultAsync();

        for (DateTime currentDate = startDate; currentDate <= endDate; currentDate = currentDate.AddDays(1))
        {
            if (currentDate.DayOfWeek == DayOfWeek.Saturday ||
                currentDate.DayOfWeek == DayOfWeek.Sunday ||
                holidays.Contains(currentDate) ||
                leaveDays.Contains(currentDate))
            {
                continue;
            }

            var applicableDedication = dedications
                .Where(d => currentDate >= d.Start && currentDate <= d.End)
                .OrderByDescending(d => d.Type)
                .Select(d => d.Reduc)
                .FirstOrDefault();

            totalPm += dailyPmValue * (1 - applicableDedication);
        }

        return Math.Round(totalPm, 2);
    }

    public async Task<Dictionary<DateTime, int>> GetWorkingDaysFromDbForRange(DateTime startDate, DateTime endDate)
    {
        var workingDaysPerMonth = new Dictionary<DateTime, int>();

        // Obtén todos los valores dentro del rango de años.
        var dailyPmValuesInRange = await _context.DailyPMValues
            .Where(dpv => dpv.Year >= startDate.Year && dpv.Year <= endDate.Year)
            .ToListAsync();

        // Filtra los valores en memoria para incluir solo aquellos dentro del rango de meses.
        var filteredValues = dailyPmValuesInRange
            .Where(dpv =>
                new DateTime(dpv.Year, dpv.Month, 1) >= startDate &&
                new DateTime(dpv.Year, dpv.Month, 1) <= endDate)
            .ToList();

        foreach (var dpv in filteredValues)
        {
            DateTime firstDayOfMonth = new DateTime(dpv.Year, dpv.Month, 1);
            workingDaysPerMonth[firstDayOfMonth] = dpv.WorkableDays;
        }

        return workingDaysPerMonth;
    }

    public async Task<List<DateTime>> GenerateMonthList(DateTime startDate, DateTime endDate)
    {
        var months = new List<DateTime>();
        DateTime currentMonth = new DateTime(startDate.Year, startDate.Month, 1);

        while (currentMonth <= endDate)
        {
            months.Add(currentMonth);
            currentMonth = currentMonth.AddMonths(1);
        }

        return months;
    }




}
