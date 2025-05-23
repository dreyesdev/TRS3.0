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
using Serilog;
using System.Text;
namespace TRS2._0.Services;

public class WorkCalendarService
{
        private readonly TRSDBContext _context;

        public WorkCalendarService(TRSDBContext context, ILogger<WorkCalendarService> @object)
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

        // Obtener los registros de bajas (Leave) para el mes y año especificados
        var leaveRecords = await _context.Leaves
            .Where(l => l.PersonId == personId && l.Day.Year == year && l.Day.Month == month)
            .ToDictionaryAsync(l => l.Day, l => new { l.LeaveReduction, l.Type });

        // Valor de PM por día obtenido de la tabla DailyPMValues
        var dailyPmValue = await _context.DailyPMValues
            .Where(d => d.Year == year && d.Month == month)
            .Select(d => d.PmPerDay)
            .FirstOrDefaultAsync();

        decimal totalPm = 0;

        // Procesar cada día del mes
        for (DateTime currentDate = new DateTime(year, month, 1); currentDate <= new DateTime(year, month, DateTime.DaysInMonth(year, month)); currentDate = currentDate.AddDays(1))
        {
            // Omitir sábados, domingos, días festivos
            if (currentDate.DayOfWeek == DayOfWeek.Saturday ||
                currentDate.DayOfWeek == DayOfWeek.Sunday ||
                holidays.Contains(currentDate))
            {
                continue;
            }

            // Obtener la reducción de baja si existe
            var leaveData = leaveRecords.TryGetValue(currentDate, out var leave) ? leave : null;
            var leaveReduction = leaveData?.LeaveReduction ?? 0;
            var leaveType = leaveData?.Type ?? 0;

            // Obtener la reducción de dedicación más actual para la fecha
            var applicableDedication = dedications
                .Where(d => currentDate >= d.Start && currentDate <= d.End)
                .OrderByDescending(d => d.Type) // Dar prioridad a la dedicación con el 'Type' más alto
                .Select(d => d.Reduc)
                .FirstOrDefault();

            // Manejar el caso específico de baja de paternidad (Type 12)
            if (leaveType == 12)
            {
                // Aplicar la reducción de baja al PM ajustado por la dedicación
                var adjustedPmForDedication = dailyPmValue * (1 - applicableDedication);
                totalPm += adjustedPmForDedication * (1 - leaveReduction); // Baja afecta al PM ya ajustado
            }
            else
            {
                // Sumar el PM ajustado por la suma de reducciones estándar
                var totalReduction = Math.Min(1, applicableDedication + leaveReduction);
                if (totalReduction < 1)
                {
                    totalPm += dailyPmValue * (1 - totalReduction);
                }
            }

            // Asegurarse de que totalPm no exceda 1
            totalPm = Math.Min(totalPm, 1);
        }

        // Redondear el PM total a 2 decimales y devolverlo
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

    public async Task<decimal> CalculateMonthlyEffortForPersonWithoutCurrentWP(int personId,int wpId, int year, int month)
    {
        // Obtener todos los esfuerzos asociados a la persona que están dentro del rango de fechas especificado
        var efforts = await _context.Persefforts
            .Include(pe => pe.WpxPersonNavigation)
            .Where(pe => pe.WpxPersonNavigation.Person == personId &&
                         pe.Month.Year == year &&
                         pe.Month.Month == month)
            .ToListAsync();

        var effortforthiswp = efforts.Where(e => e.WpxPersonNavigation.Wp == wpId).ToList();

        var sumefforts = effortforthiswp.Sum(e => e.Value);

        // Sumar los valores de esfuerzo para el mes dado
        decimal totalEffort = efforts.Sum(e => e.Value);

        totalEffort = totalEffort - sumefforts;

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

    public async Task<Dictionary<DateTime, decimal>> GetDeclaredHoursPerMonthForPersonInProyect(int personId, DateTime startDate, DateTime endDate, int projectId)
    {
        // Asegúrate de que endDate es el último momento del mes para incluir todos los días del mes
        endDate = new DateTime(endDate.Year, endDate.Month, DateTime.DaysInMonth(endDate.Year, endDate.Month));

        var hoursPerMonth = await _context.Timesheets
            .Where(ts => ts.WpxPersonNavigation.Person == personId &&
                                    ts.WpxPersonNavigation.WpNavigation.ProjId == projectId &&
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


    // -----------------------------------------------------------------------------------
    // [CHANGE] GetWorkedDaysPerMonthForPersonInProject
    // Actualizado el 30/04/2025
    // - Ahora el cálculo de días trabajados se basa en:
    //    - Horas reales declaradas en Timesheets del proyecto.
    //    - Afiliación activa en el mes con mayor número de horas por día.
    //    - División y redondeo con Math.Round(..., MidpointRounding.AwayFromZero).
    // - Si no hay afiliación válida, marca como "SIN AFILIACIÓN".
    // Esta lógica replica exactamente el método de ExportTimesheetToPdf2 en TimesheetController.
    // -----------------------------------------------------------------------------------

    public async Task<Dictionary<DateTime, decimal>> GetWorkedDaysPerMonthForPersonInProject(int personId, DateTime startDate, DateTime endDate, int projectId)
    {
        var declaredHoursPerMonth = await GetDeclaredHoursPerMonthForPersonInProyect(personId, startDate, endDate, projectId);

        var affiliations = await _context.AffxPersons
            .Where(a => a.PersonId == personId &&
                        a.Start <= endDate && a.End >= startDate)
            .ToListAsync();

        var affHoursList = await _context.AffHours.ToListAsync();

        Dictionary<DateTime, decimal> workedDaysPerMonth = new Dictionary<DateTime, decimal>();

        foreach (var entry in declaredHoursPerMonth)
        {
            DateTime month = entry.Key;
            decimal totalDeclaredHours = entry.Value;

            var monthAffiliations = affiliations
                .Where(a => a.Start <= month.AddMonths(1).AddDays(-1) && a.End >= month)
                .ToList();

            if (!monthAffiliations.Any())
            {
                workedDaysPerMonth[month] = -1; // SIN AFILIACIÓN
                continue;
            }

            // Aquí: obtener correctamente la hora diaria vigente en este mes
            List<decimal> activeAffiliationHours = new List<decimal>();

            foreach (var affiliation in monthAffiliations)
            {
                var validAffHours = affHoursList
                    .Where(ah => ah.AffId == affiliation.AffId &&
                                 ah.StartDate <= month.AddMonths(1).AddDays(-1) &&
                                 ah.EndDate >= month)
                    .ToList();

                foreach (var v in validAffHours)
                {
                    if (v.Hours > 0)
                    {
                        activeAffiliationHours.Add(v.Hours);
                    }
                }
            }

            if (!activeAffiliationHours.Any())
            {
                workedDaysPerMonth[month] = -1; // No hay afiliaciones válidas para este mes
                continue;
            }

            // Usar la afiliación de MENOS horas (como en Timesheet real)
            decimal selectedAffiliationHours = activeAffiliationHours.Min();

            decimal workedDays = Math.Round(totalDeclaredHours / selectedAffiliationHours, 1, MidpointRounding.AwayFromZero);
            workedDaysPerMonth[month] = workedDays;
        }

        return workedDaysPerMonth;
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

    public async Task<Dictionary<DateTime, decimal>> CalculateTotalHoursForPersonV2(
    int personId,
    DateTime startDate,
    DateTime endDate)
    {
        var totalHoursPerMonth = new Dictionary<DateTime, decimal>();

        startDate = new DateTime(startDate.Year, startDate.Month, 1);
        endDate = new DateTime(endDate.Year, endDate.Month, DateTime.DaysInMonth(endDate.Year, endDate.Month));

        while (startDate <= endDate)
        {
            var monthKey = new DateTime(startDate.Year, startDate.Month, 1);
            decimal hours = await CalculateMaxHoursForPersonInMonth(personId, startDate.Year, startDate.Month);
            totalHoursPerMonth[monthKey] = hours;

            startDate = startDate.AddMonths(1);
        }

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
                                gradient += "darkorange, ";
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
                            gradientParts.Add("darkorange");
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
            if (status == 0 && travelDetails.Any())
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
        var startDates = months.Select(m => new DateTime(m.Year, m.Month, 1)).ToList();
        var endDates = months.Select(m => new DateTime(m.Year, m.Month, DateTime.DaysInMonth(m.Year, m.Month))).ToList();

        Console.WriteLine($"Min Start Date: {startDates.Min()}");
        Console.WriteLine($"Max End Date: {endDates.Max()}");

        if (!startDates.Any() || !endDates.Any())
        {
            return new Dictionary<DateTime, bool>(); // Retorna un diccionario vacío si no hay fechas de inicio o fin.
        }

        var minStartDate = startDates.Min();
        var maxEndDate = endDates.Max();


        var contracts = await _context.Dedications
                        .Where(d => d.PersId == personId && d.End >= minStartDate && d.Start <= maxEndDate)
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

            // Seleccionar la dedicación con el valor Type más alto
            var currentDedication = dedications
                .Where(d => currentDate >= d.Start && currentDate <= d.End)
                .OrderByDescending(d => d.Type) // Ordenar por Type de mayor a menor
                .FirstOrDefault()?.Reduc;

            // Si no se encuentra dedicación específica o Reduc es 0.00, asume una jornada completa
            decimal dedicationFactor = currentDedication.HasValue ? currentDedication.Value : 0.00M;

            // Calcular las horas ajustadas para el día y redondearlas al entero o .5 más cercano
            decimal adjustedDailyHours = currentAffiliationHours * (1 - dedicationFactor);
            adjustedDailyHours = RoundToNearestHalfOrWhole(adjustedDailyHours);

            // Agregar al diccionario
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
    public async Task<bool> IsHoliday(DateTime date)
    {
        return await _context.NationalHolidays.AnyAsync(h => h.Date == date);
    }


    public async Task <bool> IsWeekend(DateTime date)
    {
        return date.DayOfWeek == DayOfWeek.Saturday || date.DayOfWeek == DayOfWeek.Sunday;
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
    public async Task<decimal> GetEffortForPersonInProject(int personId, int projectId, int year, int month)
    {
        // Obtener la fecha de inicio y fin del mes
        DateTime startDate = new DateTime(year, month, 1);
        DateTime endDate = startDate.AddMonths(1).AddDays(-1);

        // Obtener el esfuerzo mensual de la persona para el proyecto
        Dictionary<DateTime, decimal> monthlyEffort = await CalculateMonthlyEffortForPersonInProject(personId, startDate, endDate, projectId);

        // Obtener el esfuerzo total para el mes
        decimal totalEffort = monthlyEffort.Values.Sum();

        return totalEffort;
    }


    // Función para calcular LeaveReduction
    public async Task<decimal> CalculateLeaveReductionAsync(int personId, DateTime date, decimal totalHoursRequested)
    {
        // Buscar la afiliación de la persona para la fecha específica
        var affiliation = await _context.AffxPersons
            .FirstOrDefaultAsync(a => a.PersonId == personId && a.Start <= date && a.End >= date);

        if (affiliation == null)
        {
            throw new Exception($"No affiliation found for PersonId {personId} on {date}");
        }

        // Obtener las horas contractuales (100% del día) desde AffHours
        var affHours = await _context.AffHours
            .FirstOrDefaultAsync(ah => ah.AffId == affiliation.AffId);

        if (affHours == null)
        {
            throw new Exception($"No working hours found for AffId {affiliation.AffId}");
        }

        // Calcular el porcentaje del día trabajado
        var fullDayHours = affHours.Hours; // Horas contractuales del día
        if (fullDayHours <= 0)
        {
            throw new Exception($"Invalid working hours ({fullDayHours}) for AffId {affiliation.AffId}");
        }

        // Asegurarse de que LeaveReduction no sea mayor que 1
        var leaveReduction = Math.Min(totalHoursRequested / fullDayHours, 1.00m);

        return Math.Round(leaveReduction, 2); // Redondear a dos decimales
    }

    // Método auxiliar para redondear al entero o .5 más cercano
    private decimal RoundToNearestHalfOrWhole(decimal value)
    {
        // Multiplicar por 2, redondear al entero más cercano y dividir por 2
        return Math.Round(value * 2, MidpointRounding.AwayFromZero) / 2;
    }

    // Calcula las horas estimadas de una persona en un proyecto
    public async Task<decimal> CalculateEstimatedHoursForPersonInProject(int personId, int projectId, int year, int month)
    {
        // Obtener las horas totales trabajadas por la persona en el mes
        var hoursPerDayWithDedication = await CalculateDailyWorkHoursWithDedication(personId, year, month);
        var totalMonthlyHours = hoursPerDayWithDedication.Values.Sum();

        if (totalMonthlyHours == 0)
        {
            return 0; // Si no hay horas trabajadas, devolver 0
        }

        // Obtener los esfuerzos para los Work Packages del proyecto en el mes especificado
        var efforts = await _context.Persefforts
            .Include(pe => pe.WpxPersonNavigation)
            .Where(pe => pe.WpxPersonNavigation.Person == personId &&
                         pe.WpxPersonNavigation.WpNavigation.ProjId == projectId &&
                         pe.Month.Year == year &&
                         pe.Month.Month == month)
            .ToListAsync();

        // Calcular el esfuerzo total
        decimal totalEffort = efforts.Sum(e => e.Value);

        if (totalEffort == 0)
        {
            return 0; // Si no hay esfuerzo registrado, devolver 0
        }

        // Calcular las horas estimadas en el proyecto
        decimal estimatedHours = totalMonthlyHours * totalEffort;

        // Redondear al entero o .5 más cercano
        return RoundToNearestHalfOrWhole(estimatedHours);
    }

    // Calcula las horas estimadas de una persona en un proyecto y en un paquete de trabajo específico
    public async Task<decimal> CalculateEstimatedHoursForPersonInWorkPackage(int personId, int wpId, int year, int month)
    {
        // Obtener las horas totales trabajadas por la persona en el mes
        var hoursPerDayWithDedication = await CalculateDailyWorkHoursWithDedication(personId, year, month);
        var totalMonthlyHours = hoursPerDayWithDedication.Values.Sum();

        if (totalMonthlyHours == 0)
        {
            return 0; // Si no hay horas trabajadas, devolver 0
        }

        // Obtener los esfuerzos para el Work Package específico en el mes especificado
        var efforts = await _context.Persefforts
            .Where(pe => pe.WpxPersonNavigation.Person == personId &&
                                    pe.WpxPersonNavigation.Wp == wpId &&
                                                            pe.Month.Year == year &&
                                                                                    pe.Month.Month == month)
            .ToListAsync();

        // Calcular el esfuerzo total
        decimal totalEffort = efforts.Sum(e => e.Value);

        if (totalEffort == 0)
        {
            return 0; // Si no hay esfuerzo registrado, devolver 0
        }

        // Calcular las horas estimadas en el paquete de trabajo
        decimal estimatedHours = totalMonthlyHours * totalEffort;

        // Redondear al entero o .5 más cercano
        return RoundToNearestHalfOrWhole(estimatedHours);
    }

    public async Task<string> AdjustEffortAsync(int wpId, int personId, DateTime month)
    {
        // Inicio del mes y fin del mes
        DateTime startOfMonth = new DateTime(month.Year, month.Month, 1);
        DateTime endOfMonth = startOfMonth.AddMonths(1).AddDays(-1);

        // Validar si hay horas registradas en Timesheets
        var wpxPerson = await _context.Wpxpeople
            .FirstOrDefaultAsync(wpx => wpx.Wp == wpId && wpx.Person == personId);

        if (wpxPerson == null)
        {
            return "Error: No se encontró la relación entre la persona y el paquete de trabajo.";
        }

        var totalHoursInTimesheets = await _context.Timesheets
            .Where(ts => ts.WpxPersonId == wpxPerson.Id && ts.Day >= startOfMonth && ts.Day <= endOfMonth)
            .SumAsync(ts => (decimal?)ts.Hours) ?? 0;

        if (totalHoursInTimesheets == 0)
        {
            return "Error: No hay horas registradas para esta persona en este paquete de trabajo y mes.";
        }

        // Obtener el máximo de horas para la persona en el mes usando la nueva función
        decimal maxHours = await CalculateMaxHoursForPersonInMonth(personId, month.Year, month.Month);

        if (maxHours == 0)
        {
            return "Error: No se encontraron horas máximas definidas para las afiliaciones de esta persona.";
        }

        // Calcular el porcentaje de esfuerzo
        decimal effortPercentage = totalHoursInTimesheets / maxHours;

        // Ajustar o crear el registro en Perseffort
        var existingEffort = await _context.Persefforts
            .FirstOrDefaultAsync(pe => pe.WpxPerson == wpxPerson.Id && pe.Month == startOfMonth);

        if (existingEffort != null)
        {
            existingEffort.Value = effortPercentage;
            _context.Persefforts.Update(existingEffort);
        }
        else
        {
            var newEffort = new Perseffort
            {
                WpxPerson = wpxPerson.Id,
                Month = startOfMonth,
                Value = effortPercentage
            };
            _context.Persefforts.Add(newEffort);
        }

        await _context.SaveChangesAsync();

        return $"Éxito: El esfuerzo para la persona {personId} en el paquete de trabajo {wpId} ha sido ajustado al {effortPercentage:P2} para el mes {month:MMMM yyyy}.";
    }

    
    public async Task<decimal> CalculateMaxHoursForPersonInMonth(int personId, int year, int month)
    {
        // Obtener afiliaciones activas de la persona para el mes
        var affiliations = await _context.AffxPersons
            .Where(ap => ap.PersonId == personId && ap.Start <= new DateTime(year, month, DateTime.DaysInMonth(year, month)) && ap.End >= new DateTime(year, month, 1))
            .Select(ap => new
            {
                ap.AffId,
                StartDate = ap.Start,
                EndDate = ap.End
            })
            .ToListAsync();

        if (!affiliations.Any())
        {
            return 0; // No hay afiliaciones activas
        }

        // Caso de una sola afiliación que cubre todo el mes
        if (affiliations.Count == 1 && affiliations[0].StartDate <= new DateTime(year, month, 1))
        {
            var dailyHours = await _context.AffHours
                .Where(ah => ah.AffId == affiliations[0].AffId && ah.StartDate <= new DateTime(year, month, DateTime.DaysInMonth(year, month)) && ah.EndDate >= new DateTime(year, month, 1))
                .Select(ah => ah.Hours)
                .FirstOrDefaultAsync();

            if (dailyHours > 0)
            {
                int workingDays = await GetWorkingDaysInMonth(year, month);
                return workingDays * dailyHours;
            }
        }

        decimal totalMaxHours = 0;

        // Iterar por cada día del mes para múltiples afiliaciones
        DateTime startOfMonth = new DateTime(year, month, 1);
        DateTime endOfMonth = startOfMonth.AddMonths(1).AddDays(-1);
        for (DateTime currentDay = startOfMonth; currentDay <= endOfMonth; currentDay = currentDay.AddDays(1))
        {
            // Saltar sábados, domingos y días festivos
            var nationalHolidays = await _context.NationalHolidays
                .Where(nh => nh.Date >= startOfMonth && nh.Date <= endOfMonth)
                .Select(nh => nh.Date)
                .ToListAsync();

            if (currentDay.DayOfWeek == DayOfWeek.Saturday || currentDay.DayOfWeek == DayOfWeek.Sunday || nationalHolidays.Contains(currentDay))
            {
                continue;
            }

            // Determinar la afiliación activa para el día
            var activeAffiliation = affiliations
                .FirstOrDefault(aff => aff.StartDate <= currentDay && aff.EndDate >= currentDay);

            if (activeAffiliation != null)
            {
                // Obtener las horas diarias de la afiliación activa
                var dailyHours = await _context.AffHours
                    .Where(ah => ah.AffId == activeAffiliation.AffId && ah.StartDate <= currentDay && ah.EndDate >= currentDay)
                    .Select(ah => ah.Hours)
                    .FirstOrDefaultAsync();

                if (dailyHours > 0)
                {
                    totalMaxHours += dailyHours;
                }
            }
        }

        return totalMaxHours;
    }

    public async Task<int> GetWorkingDaysInMonth(int year, int month)
    {
        // Fecha de inicio y fin del mes
        DateTime startOfMonth = new DateTime(year, month, 1);
        DateTime endOfMonth = startOfMonth.AddMonths(1).AddDays(-1);

        // Obtener días festivos nacionales
        var nationalHolidays = await _context.NationalHolidays
            .Where(nh => nh.Date >= startOfMonth && nh.Date <= endOfMonth)
            .Select(nh => nh.Date)
            .ToListAsync();

        int workingDays = 0;

        // Iterar por cada día del mes
        for (DateTime currentDay = startOfMonth; currentDay <= endOfMonth; currentDay = currentDay.AddDays(1))
        {
            // Contar solo días laborables
            if (currentDay.DayOfWeek != DayOfWeek.Saturday && currentDay.DayOfWeek != DayOfWeek.Sunday && !nationalHolidays.Contains(currentDay))
            {
                workingDays++;
            }
        }

        return workingDays;
    }

    public async Task AutoFillTimesheetForPersonAndMonthAsync(int personId, DateTime targetMonth)
    {
        var logPath = Path.Combine(Directory.GetCurrentDirectory(), "Logs", $"AutoFillTimesheet_{personId}_{targetMonth:yyyyMM}.txt");
        var logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.File(logPath, rollingInterval: RollingInterval.Infinite)
            .CreateLogger();

        try
        {
            var monthStart = new DateTime(targetMonth.Year, targetMonth.Month, 1);
            var monthEnd = monthStart.AddMonths(1).AddDays(-1);

            logger.Information($"Inicio del proceso para el empleado {personId} en el mes {monthStart:yyyy-MM}");

            var wpxList = await _context.Wpxpeople
                .Include(w => w.PersonNavigation)
                .Include(w => w.WpNavigation)
                    .ThenInclude(wp => wp.Proj)
                .Where(w => w.Person == personId)
                .ToListAsync();

            var wpxWithEffort = await _context.Persefforts
                .Where(pe => pe.Month == monthStart && wpxList.Select(w => w.Id).Contains(pe.WpxPerson))
                .Select(pe => pe.WpxPerson)
                .FirstOrDefaultAsync();

            if (wpxWithEffort == 0)
            {
                logger.Warning($"Empleado {personId} no tiene effort registrado en {monthStart:yyyy-MM}");
                return;
            }

            var wpx = wpxList.FirstOrDefault(w => w.Id == wpxWithEffort);
            if (wpx == null)
            {
                logger.Warning($"Error al obtener WP con effort para el empleado {personId} en {monthStart:yyyy-MM}");
                return;
            }

            var personName = wpx.PersonNavigation.Name ?? "SIN NOMBRE";
            var personSurname = wpx.PersonNavigation.Surname ?? "SIN APELLIDO";
            var projectAcronym = wpx.WpNavigation?.Proj?.Acronim ?? "SIN PROYECTO";

            var monthlyEffort = await _context.Persefforts
                .Where(pe => pe.WpxPerson == wpx.Id && pe.Month == monthStart)
                .SumAsync(pe => pe.Value);

            if (monthlyEffort <= 0)
            {
                logger.Warning($"[{personName} {personSurname}] [{projectAcronym}] - WP={wpx.Wp}, Mes={monthStart:yyyy-MM}: Effort es 0 o negativo");
                return;
            }

            var maxEffort = await _context.PersMonthEfforts
                .Where(pme => pme.PersonId == personId && pme.Month == monthStart)
                .Select(pme => pme.Value)
                .FirstOrDefaultAsync();

            if (monthlyEffort > maxEffort)
            {
                logger.Warning($"[{personName} {personSurname}] [{projectAcronym}] - WP={wpx.Wp}, Mes={monthStart:yyyy-MM}: Ajuste de {monthlyEffort} a {maxEffort}");
                monthlyEffort = maxEffort;
            }

            bool ajusteCompleto = Math.Abs(monthlyEffort - maxEffort) <= 0.001m;

            var validWorkDays = Enumerable.Range(0, (monthEnd - monthStart).Days + 1)
                .Select(offset => monthStart.AddDays(offset))
                .Where(day => day.DayOfWeek != DayOfWeek.Saturday && day.DayOfWeek != DayOfWeek.Sunday)
                .ToList();

            validWorkDays = validWorkDays
                .Where(day => !IsHoliday(day).Result)
                .ToList();

            var leaveDays = await _context.Leaves
                .Where(l => l.PersonId == personId && l.Day >= monthStart && l.Day <= monthEnd && (l.Type == 1 || l.Type == 2 || l.Type == 3))
                .Select(l => l.Day)
                .ToListAsync();

            validWorkDays = validWorkDays.Except(leaveDays).ToList();

            if (!validWorkDays.Any())
            {
                logger.Warning($"[{personName} {personSurname}] [{projectAcronym}] - No hay días hábiles disponibles");
                return;
            }

            var maxHoursForMonth = await CalculateMaxHoursForPersonInMonth(personId, monthStart.Year, monthStart.Month);

            decimal totalMonthlyHours = ajusteCompleto ? maxHoursForMonth * maxEffort : monthlyEffort * maxHoursForMonth;
            decimal rawDailyHours = totalMonthlyHours / validWorkDays.Count;
            //decimal adjustedDailyHours = Math.Round(rawDailyHours * 2, MidpointRounding.AwayFromZero) / 2; // ANTERIOR AL CAMBIO DECIMAL
            decimal adjustedDailyHours = Math.Round(rawDailyHours, 2, MidpointRounding.AwayFromZero);

            if (adjustedDailyHours == 0) adjustedDailyHours = 0.5m;

            foreach (var day in validWorkDays)
            {
                var timesheet = await _context.Timesheets
                    .FirstOrDefaultAsync(ts => ts.WpxPersonId == wpx.Id && ts.Day == day);

                if (timesheet == null)
                {
                    _context.Timesheets.Add(new Timesheet
                    {
                        WpxPersonId = wpx.Id,
                        Day = day,
                        Hours = adjustedDailyHours
                    });
                    logger.Information($"[{personName} {personSurname}] Día {day:yyyy-MM-dd}: nuevo registro con {adjustedDailyHours} horas");
                }
                else
                {
                    decimal previous = timesheet.Hours;
                    timesheet.Hours = adjustedDailyHours;
                    logger.Information($"[{personName} {personSurname}] Día {day:yyyy-MM-dd}: actualizado de {previous} a {adjustedDailyHours} horas");
                }
            }

            await _context.SaveChangesAsync();
            logger.Information($"Finalizado para el empleado {personId} en {monthStart:yyyy-MM}");
        }
        catch (Exception ex)
        {
            logger.Error($"Error en AutoFillTimesheetForPersonAndMonthAsync: {ex.Message}");
        }
        finally
        {
            logger.Dispose();
        }
    }

    public async Task<bool> HasNoContractDaysAsync(int personId, int year, int month)
    {
        var startDate = new DateTime(year, month, 1);
        var endDate = startDate.AddMonths(1).AddDays(-1);

        var hasNoContract = await _context.Leaves.AnyAsync(l =>
            l.PersonId == personId &&
            l.Day >= startDate &&
            l.Day <= endDate &&
            l.Type == 3 // tipo "no contrato"
        );

        return hasNoContract;
    }


    public async Task<decimal> CalculateMaxHoursByAffiliationOnlyAsync(int personId, int year, int month)
    {
        var startDate = new DateTime(year, month, 1);
        var endDate = startDate.AddMonths(1).AddDays(-1);

        // Buscar afiliación válida para ese mes
        var affx = await _context.AffxPersons
            .Where(ap => ap.PersonId == personId &&
                         ap.Start <= endDate &&
                         ap.End >= startDate)
            .OrderByDescending(ap => ap.Start) // Por si hay varias, tomar la más reciente
            .FirstOrDefaultAsync();

        if (affx == null)
            return 0;

        int affId = affx.AffId;

        // Buscar las horas asociadas a esa afiliación
        var affHours = await _context.AffHours
            .Where(ah => ah.AffId == affId &&
                         ah.StartDate <= endDate &&
                         ah.EndDate >= startDate)
            .OrderByDescending(ah => ah.StartDate)
            .FirstOrDefaultAsync();

        if (affHours == null)
            return 0;

        // Calcular días laborables
        int workingDays = await CalculateWorkingDays(year, month);

        return Math.Round(affHours.Hours * workingDays, 2);
    }



    // Esta función ajusta automáticamente los efforts mensuales de una persona para corregir overloads respetando viajes y bloqueos
    // Esta función ajusta automáticamente los efforts mensuales de una persona para corregir overloads respetando viajes y bloqueos
    public async Task<(bool Success, string Message)> AdjustMonthlyOverloadAsync(int personId, int year, int month)
    {
        var log = new StringBuilder();
        var result = (Success: false, Message: "");

        // Buscar el PM máximo permitido para esa persona y mes
        var pmEntry = await _context.PersMonthEfforts
                        .FirstOrDefaultAsync(p => p.PersonId == personId && p.Month.Year == year && p.Month.Month == month);

        // Si no hay PM registrado, no se puede continuar
        if (pmEntry == null)
            return (false, "No PM value found for the specified month.");

        var pmValue = pmEntry.Value;

        // Definir las fechas de inicio y fin del mes
        DateTime monthStart = new DateTime(year, month, 1);
        DateTime monthEnd = monthStart.AddMonths(1).AddDays(-1);

        // Obtener todos los efforts de la persona ese mes
        var efforts = await _context.Persefforts
            .Include(pe => pe.WpxPersonNavigation)
            .ThenInclude(wpx => wpx.WpNavigation)
            .ThenInclude(wp => wp.Proj)
            .Where(pe => pe.WpxPersonNavigation.Person == personId &&
                         pe.Month.Year == year && pe.Month.Month == month)
            .ToListAsync();

        // Si no hay efforts, no hay nada que ajustar
        if (!efforts.Any())
            return (false, "No efforts found for this person and month.");

        // Guardar los valores originales de los efforts antes de resetearlos
        var originalEffortValues = efforts.ToDictionary(e => e.Code, e => e.Value);

        // Agrupar efforts por proyecto y WP para analizarlos por separado
        var grouped = efforts
            .GroupBy(e => new { ProjId = e.WpxPersonNavigation.WpNavigation.ProjId, WpId = e.WpxPersonNavigation.Wp })
            .Select(g => new
            {
                ProjId = g.Key.ProjId,
                WpId = g.Key.WpId,
                TotalEffort = g.Sum(x => x.Value),
                Persefforts = g.ToList()
            }).ToList();

        // Calcular el total de effort actualmente alocado
        var totalAllocated = grouped.Sum(g => g.TotalEffort);
        if (totalAllocated <= pmValue)
            return (true, "No overload found.");

        var overload = totalAllocated - pmValue; // Calcula cuánto sobra

        // Buscar los proyectos bloqueados para esa persona y mes que no se pueden modificar
        var lockedProjects = await _context.ProjectMonthLocks
            .Where(l => l.PersonId == personId && l.Year == year && l.Month == month && l.IsLocked)
            .Select(l => l.ProjectId)
            .ToListAsync();

        // Identificar efforts bloqueados
        var lockedEfforts = grouped.Where(g => lockedProjects.Contains(g.ProjId)).ToList();

        // Calcular effort total bloqueado y disponible
        var lockedEffortTotal = lockedEfforts.Sum(g => g.TotalEffort);
        var availableEffort = pmValue - lockedEffortTotal;

        // Si lo bloqueado ya supera el PM, no se puede resolver
        if (availableEffort < 0)
            return (false, "Locked efforts exceed available PM. Overload cannot be resolved.");

        // Filtrar los efforts que sí se pueden modificar
        var modifiableEfforts = grouped.Except(lockedEfforts).ToList();

        // Obtener los viajes aceptados en ese mes para esa persona
        var travels = await _context.Liquidations
            .Where(t => t.PersId == personId && t.Start <= monthEnd && t.End >= monthStart)
            .Select(t => t.Id)
            .ToListAsync();

        // Obtener los IDs
        var travelProjIds = await _context.liqdayxproject
        .Where(l => travels.Contains(l.LiqId) && l.Day >= monthStart && l.Day <= monthEnd)
        .Select(l => l.ProjId)
        .Distinct()
        .ToListAsync(); // Obtiene los Proyectos afectados por viajes /

        // Agrupar esos WPs por proyecto
        var withTravelGroupedByProject = modifiableEfforts
        .Where(g => travelProjIds.Contains(g.ProjId))
        .GroupBy(g => g.ProjId)
        .ToList(); // Agrupa por proyecto los WPs con viajes

        var minEffortByProject = new Dictionary<int, decimal>(); // Diccionario de esfuerzo mínimo requerido por proyecto

        // Calcular el mínimo effort requerido por proyecto para justificar los viajes
        foreach (var projectGroup in withTravelGroupedByProject)
        {
            int projId = projectGroup.Key;
            var travelEffort = await _context.liqdayxproject
                .Where(l => l.PersId == personId &&
                            l.ProjId == projId &&
                            l.Day >= monthStart && l.Day <= monthEnd)
                .SumAsync(l => l.PMs);

            minEffortByProject[projId] = travelEffort; // Guarda el mínimo por proyecto
        }

        // Sumar todos los efforts mínimos por viajes
        var minTotalTravelEffort = minEffortByProject.Values.Sum(); // Suma total de esfuerzo mínimo requerido por viajes
        if (availableEffort < minTotalTravelEffort) // Si no hay suficiente disponible, salir
            return (false, "Available PM is not enough to justify travel-related efforts.");

        // Calcular el esfuerzo total a reducir y el ratio de reducción general
        decimal totalEffortToReduce = modifiableEfforts.Sum(m => m.TotalEffort); // Total que se podría ajustar
        decimal targetEffort = availableEffort; // Esfuerzo que realmente podemos usar
        decimal reductionRatio = Math.Min(1.0m, targetEffort / totalEffortToReduce); // Ratio general de reducción

        // Enfoque iterativo: ajustamos individualmente los proyectos con viaje que no pueden aceptar el ratio global,
        // y una vez viable, aplicamos el ratio a todos los que sí pueden aceptarlo
        var remainingTravelEfforts = withTravelGroupedByProject.ToList(); // Lista inicial de proyectos con viaje por tratar
                                                                          
        var treatedProjects = new HashSet<int>();// Conjunto para registrar los IDs de proyectos ya ajustados por viaje (personalizado o global)

        while (true)
        {
            // Identificar proyectos que no cumplen con el mínimo usando el ratio global aplicado effort por effort
            var nonCompliantProjects = remainingTravelEfforts
                .Where(projectGroup =>
                {
                    int projId = projectGroup.Key;
                    var projectedTotal = projectGroup.SelectMany(wp => wp.Persefforts)
                        .Sum(e => Math.Round(originalEffortValues[e.Code] * reductionRatio, 2));
                    return projectedTotal < minEffortByProject[projId];
                })
                .ToList();

            if (!nonCompliantProjects.Any())
                break;

            // Para cada proyecto no compatible, aplicar ratio mínimo necesario individualmente
            foreach (var projectGroup in nonCompliantProjects)
            {
                int projId = projectGroup.Key;
                decimal minEffort = minEffortByProject[projId];
                var allEfforts = projectGroup.SelectMany(wp => wp.Persefforts).ToList();

                var totalOriginal = allEfforts.Sum(e => originalEffortValues[e.Code]);

                if (totalOriginal == 0)
                {
                    Console.WriteLine($"❌ Proyecto {projId} tiene esfuerzo original 0 pero requiere mínimo {minEffort}");
                    return (false, $"Project {projId} has zero original effort but requires travel effort.");
                }

                decimal projRatio = minEffort / totalOriginal;

                Console.WriteLine($"=== Ajustando Proyecto {projId} ===");
                Console.WriteLine($"- Ratio global original: {reductionRatio}");
                Console.WriteLine($"- No acepta el ratio global. Se aplica ratio personalizado: {projRatio}");
                Console.WriteLine($"- Esfuerzo requerido para viaje: {minEffort}");
                Console.WriteLine($"- Total effort original del proyecto: {totalOriginal}");

                decimal totalAdjusted = 0;
                var orderedEfforts = allEfforts.OrderBy(e => originalEffortValues[e.Code]).ToList();

                foreach (var effort in orderedEfforts)
                {
                    var original = originalEffortValues[effort.Code];
                    var newEffort = Math.Round(original * projRatio, 2);
                    effort.Value = newEffort;
                    totalAdjusted += newEffort;
                    Console.WriteLine($"   • Effort {effort.Code}: original={original} → ajustado={newEffort}");
                }

                // Delta final
                var delta = Math.Round(minEffort, 2) - totalAdjusted;
                if (delta != 0 && orderedEfforts.Any())
                {
                    var maxEffort = allEfforts
                                    .OrderByDescending(e => originalEffortValues[e.Code])
                                    .FirstOrDefault();
                    if (maxEffort != null)
                        maxEffort.Value += delta;
                    Console.WriteLine($"   → Se aplicó delta de ajuste final: {delta} al último effort {orderedEfforts.Last().Code}");
                }

                var finalSum = orderedEfforts.Sum(e => e.Value);
                Console.WriteLine($"- Total final asignado al proyecto: {finalSum}");

                if (finalSum < minEffort)
                    Console.WriteLine($"❌ El total final no cumple con el mínimo ({finalSum} < {minEffort})");
                else
                    Console.WriteLine($"✅ Mínimo cumplido correctamente ({finalSum} ≥ {minEffort})");

                availableEffort -= minEffort;
                // Marcar este proyecto como tratado
                treatedProjects.Add(projId);

            }


            remainingTravelEfforts = remainingTravelEfforts.Except(nonCompliantProjects).ToList();
            totalEffortToReduce = modifiableEfforts.SelectMany(m => m.Persefforts).Sum(e => originalEffortValues[e.Code]) - (pmValue - availableEffort);
            reductionRatio = Math.Min(1.0m, availableEffort / totalEffortToReduce);
        }
                
        // Ajustamos el resto de proyectos con viaje usando el ratio global validado
        foreach (var projectGroup in remainingTravelEfforts)
        {
            int projId = projectGroup.Key;
            var minEffort = minEffortByProject.ContainsKey(projId) ? minEffortByProject[projId] : 0;
            var allEfforts = projectGroup.SelectMany(wp => wp.Persefforts).ToList();
            var totalOriginal = allEfforts.Sum(e => originalEffortValues[e.Code]);
            // ⚠️ IMPORTANTE: Calculamos idealTotal sumando los efforts ya redondeados individualmente.
            // Esto evita errores acumulados por redondeos tardíos como Math.Round(suma * ratio, 2),
            // que pueden producir diferencias de ±0.01 incluso si los valores individuales son correctos.
            // Este enfoque asegura consistencia con la forma en que realmente se aplican los efforts.
            var idealTotal = allEfforts.Sum(e => Math.Round(originalEffortValues[e.Code] * reductionRatio, 2));



            Console.WriteLine($"=== Ajustando con ratio global Proyecto {projId} ===");
            Console.WriteLine($"- Total esfuerzo original: {totalOriginal}");
            Console.WriteLine($"- Ratio global aplicado: {reductionRatio}");
            Console.WriteLine($"- Ideal total esperado tras ajuste: {idealTotal}");
            if (minEffort > 0)
                Console.WriteLine($"- Mínimo requerido por viaje: {minEffort}");

            decimal totalAdjusted = 0;

            var orderedEfforts = allEfforts.OrderBy(e => originalEffortValues[e.Code]).ToList();
            foreach (var effort in orderedEfforts)
            {
                var original = originalEffortValues[effort.Code];
                var newEffort = Math.Round(original * reductionRatio, 2);
                effort.Value = newEffort;
                totalAdjusted += newEffort;
                Console.WriteLine($"   • Effort {effort.Code}: original={original} → ajustado={newEffort}");
            }

            var delta = idealTotal - totalAdjusted;
            if (delta != 0 && orderedEfforts.Any())
            {
                var maxEffort = allEfforts
                                .OrderByDescending(e => originalEffortValues[e.Code])
                                .FirstOrDefault();
                if (maxEffort != null)
                    maxEffort.Value += delta;
                Console.WriteLine($"   → Se aplicó delta de ajuste final: {delta} al último effort {orderedEfforts.Last().Code}");
            }

            var finalSum = orderedEfforts.Sum(e => e.Value);
            Console.WriteLine($"- Total final ajustado del proyecto: {finalSum}");

            if (minEffort > 0 && finalSum < minEffort)
                Console.WriteLine($"❌ NO SE CUMPLE el mínimo requerido ({finalSum} < {minEffort})");
            else if (minEffort > 0)
                Console.WriteLine($"✅ Mínimo cumplido correctamente ({finalSum} ≥ {minEffort})");

            // 🔄 Restar del esfuerzo disponible el total ajustado de este proyecto
            availableEffort -= finalSum;

            // Marcar este proyecto como tratado
            treatedProjects.Add(projId);
        }

        Console.WriteLine("=== Proyectos tratados ===");
        foreach (var id in treatedProjects)
            Console.WriteLine($"Tratado: ProjId {id}");

        // Ahora tratamos los efforts sin viajes ni ajustes anteriores de forma global (ordenados de menor a mayor effort)
        var withoutTravel = modifiableEfforts
            .Where(g => !treatedProjects.Contains(g.ProjId)) // Excluimos también proyectos ya tratados
            .ToList();
        Console.WriteLine("=== Proyectos en withoutTravel ===");
        foreach (var wp in withoutTravel)
            Console.WriteLine($"ProjId {wp.ProjId} - WpId {wp.WpId}");

        decimal remainingEffort = availableEffort; // Ya fue calculado correctamente antes
        

        var totalWithoutTravel = withoutTravel.Sum(w => w.TotalEffort);

        if (totalWithoutTravel > 0 && remainingEffort > 0)
        {
            var ratio = Math.Min(1.0m, remainingEffort / totalWithoutTravel);
            var allEfforts = withoutTravel.SelectMany(wp => wp.Persefforts).ToList();

            decimal totalAdjusted = 0;
            foreach (var effort in allEfforts)
            {
                var newEffort = Math.Round(originalEffortValues[effort.Code] * ratio, 2);
                effort.Value = newEffort;
                totalAdjusted += newEffort;
            }

            var delta = Math.Round(remainingEffort, 2) - totalAdjusted; // Compensamos la diferencia final exacta
            if (delta != 0 && allEfforts.Any())
            {
                var maxEffort = allEfforts
                    .OrderByDescending(e => originalEffortValues[e.Code])
                    .FirstOrDefault();
                if (maxEffort != null)
                    maxEffort.Value += delta;
            }

        }

        // Validación final: comprobar si el esfuerzo total corregido no excede el PM permitido
        var adjustedEfforts = modifiableEfforts.SelectMany(g => g.Persefforts).ToList();
        var totalEffortFinal = lockedEfforts.Sum(g => g.TotalEffort) + adjustedEfforts.Sum(e => e.Value);
        var excess = Math.Round(totalEffortFinal - pmValue, 2);

        if (excess > 0) // Tolerancia mínima
        {
            var maxEffort = adjustedEfforts.OrderByDescending(e => e.Value).FirstOrDefault();
            if (maxEffort != null)
            {
                maxEffort.Value = Math.Max(0, Math.Round(maxEffort.Value - excess, 2));
                totalEffortFinal = lockedEfforts.Sum(g => g.TotalEffort) + adjustedEfforts.Sum(e => e.Value);
            }
        }

        if (Math.Round(totalEffortFinal, 2) > Math.Round(pmValue, 2))
            return (false, $"Adjusted total effort ({totalEffortFinal}) exceeds available PM ({pmValue}).");

        Console.WriteLine("=== Resultado final de todos los efforts ===");        
        // Guardar todos los cambios en la base de datos
        await _context.SaveChangesAsync();
        return (true, "Overload corrected successfully.");
    }



}
