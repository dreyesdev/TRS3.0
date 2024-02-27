using TRS2._0.Models.DataModels;
using TRS2._0.Services;
using static TRS2._0.Models.ViewModels.PersonnelEffortPlanViewModel;

namespace TRS2._0.Models.ViewModels
{
    public class TimesheetViewModel
    {
        public Personnel Person { get; set; }        
        
        public List<WorkPackageInfoTS> WorkPackages { get; set; } = new List<WorkPackageInfoTS>();

        public WorkCalendarService WorkCalendarService { get; set; } = null!;

        public List<Leave> LeavesthisMonth { get; set; } = new List<Leave>();

        public List<TravelDetails> TravelsthisMonth { get; set; } = new List<TravelDetails>();

        public List<DateTime> MonthDays { get; set; } = new List<DateTime>();

        public int CurrentYear { get; set; }

        public int CurrentMonth { get; set; }

        public Dictionary<DateTime, decimal> HoursPerDay { get; set; } = new Dictionary<DateTime, decimal>();
        
        public Dictionary<DateTime, decimal> HoursPerDayWithDedication { get; set; } = new Dictionary<DateTime, decimal>();

        public decimal TotalHours { get; set; }

        public Dictionary<DateTime, decimal> TotalHoursWithDedication { get; set; } = new Dictionary<DateTime, decimal>();

        public decimal HoursUsed { get; set; }

        public List<DateTime> Holidays { get; set; } = new List<DateTime>();


    }

    public class WorkPackageInfoTS
    {
        public int WpId { get; set; }
        public string WpName { get; set; }
        public string WpTitle { get; set; }
        public string ProjectName { get; set; }
        public string ProjectSAPCode { get; set; }

        public int ProjectId { get; set; }
        public decimal Effort { get; set; }

        public decimal EstimatedHours { get; set; } // Nueva propiedad para las horas estimadas

        public List<Timesheet> Timesheets { get; set; } = new List<Timesheet>();

    }
        
}
