using Quartz;
using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TRS2._0.Services;

[DisallowConcurrentExecution]
public class TimesheetReminderJob : IJob
{
    private readonly ReminderService _reminderService;
    private readonly ILogger<TimesheetReminderJob> _logger;
    private static readonly TimeZoneInfo MadridTz =
        TimeZoneInfo.FindSystemTimeZoneById("Europe/Madrid");

    public TimesheetReminderJob(ReminderService reminderService, ILogger<TimesheetReminderJob> logger)
    {
        _reminderService = reminderService;
        _logger = logger;
    }

    public async Task Execute(IJobExecutionContext context)
    {
        try
        {
            var nowUtc = DateTime.UtcNow;
            var today = TimeZoneInfo.ConvertTimeFromUtc(nowUtc, MadridTz).Date;

            bool isFirstMonday = IsFirstMondayOfMonth(today);
            _logger.LogInformation("[TimesheetReminderJob] Ejecutando recordatorio. Fecha: {Date}, FirstMonday: {First}",
                today.ToString("yyyy-MM-dd"), isFirstMonday);

            await _reminderService.SendTimesheetRemindersAsync(isFirstMonday);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[TimesheetReminderJob] Error al ejecutar el recordatorio");
        }
    }

    private static bool IsFirstMondayOfMonth(DateTime date)
    {
        if (date.DayOfWeek != DayOfWeek.Monday) return false;
        var firstOfMonth = new DateTime(date.Year, date.Month, 1);
        var offset = ((int)DayOfWeek.Monday - (int)firstOfMonth.DayOfWeek + 7) % 7;
        var firstMonday = firstOfMonth.AddDays(offset);
        return date.Date == firstMonday.Date;
    }
}


