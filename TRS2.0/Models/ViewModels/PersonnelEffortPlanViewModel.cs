using TRS2._0.Models.DataModels;
using TRS2._0.Services;

namespace TRS2._0.Models.ViewModels
{
    public class PersonnelEffortPlanViewModel
    {
        public Perseffort Perseffort { get; set; } = null!;
        public Project Project { get; set; } = null!;
        public DateTime ProjectStartDate { get; set; }
        public DateTime ProjectEndDate { get; set; }
        public List<WorkPackageInfo> WorkPackages { get; set; } = new List<WorkPackageInfo>();

        public WorkCalendarService WorkCalendarService { get; set; } = null!;
        public List<DateTime> UniqueMonths => GetUniqueMonths();
        public List<DateTime> UniqueMonthsforPerson => GetUniqueMonthsforPerson();

        public List<DateTime> GetUniqueMonths()
        {
            if (Project == null || !Project.Start.HasValue)
                return new List<DateTime>();

            var start = new DateTime(Project.Start.Value.Year, Project.Start.Value.Month, 1);
            var end = new DateTime(Project.EndReportDate.Year, Project.EndReportDate.Month, 1);

            var months = new List<DateTime>();
            for (var date = start; date <= end; date = date.AddMonths(1))
            {
                months.Add(date);
            }

            return months;
        }


        public List<DateTime> GetMonthsForProject()
        {
            var months = new List<DateTime>();

            // Si son no-nullable, necesitamos decidir si "traen valor" comparando con default(DateTime)
            bool hasStartOverride = ProjectStartDate != default(DateTime);
            bool hasEndOverride = ProjectEndDate != default(DateTime);

            DateTime startDate = hasStartOverride
                ? ProjectStartDate
                : (Project.Start ?? DateTime.MinValue);

            DateTime endDate = hasEndOverride
                ? ProjectEndDate
                : Project.EndReportDate;

            // Normaliza a inicio/fin de mes
            startDate = new DateTime(startDate.Year, startDate.Month, 1);
            endDate = new DateTime(endDate.Year, endDate.Month, DateTime.DaysInMonth(endDate.Year, endDate.Month));

            for (var dt = startDate; dt <= endDate; dt = dt.AddMonths(1))
                months.Add(dt);

            return months;
        }



        public List<DateTime> GetUniqueMonthsforPerson()
        {
            var uniqueMonths = new HashSet<DateTime>();
            foreach (var projectEffort in ProjectsPersonnelEfforts)
            {
                foreach (var wpInfo in projectEffort.WorkPackages)
                {
                    for (var date = wpInfo.StartDate; date <= wpInfo.EndDate; date = date.AddMonths(1))
                    {
                        uniqueMonths.Add(new DateTime(date.Year, date.Month, 1));
                    }
                }
            }
            return uniqueMonths.OrderBy(d => d).ToList();
        }

        public List<DateTime> GetUniqueMonthsforPersononProject()
        {
            var start = ProjectStartDate;
            var end = ProjectEndDate;
            var uniqueMonths = new List<DateTime>();

            for (var dt = new DateTime(start.Year, start.Month, 1); dt <= end; dt = dt.AddMonths(1))
            {
                uniqueMonths.Add(dt);
            }

            return uniqueMonths;
        }

        public int GetMonthIndex(DateTime month)
        {
            return UniqueMonths.IndexOf(month) + 1;
        }

        public List<ProjectPersonnelEffort> ProjectsPersonnelEfforts { get; set; } = new List<ProjectPersonnelEffort>();

        public class ProjectPersonnelEffort
        {
            public int ProjectId { get; set; }
            public string ProjectName { get; set; }
            public List<WorkPackageInfo> WorkPackages { get; set; } = new List<WorkPackageInfo>();
        }
        public class WorkPackageInfo
        {
            public int WpId { get; set; }
            public string WpName { get; set; }
            public DateTime StartDate { get; set; }
            public DateTime EndDate { get; set; }
            public List<PersonnelEffort> PersonnelEfforts { get; set; } = new List<PersonnelEffort>();

            public List<DateTime> ActiveMonths => GetActiveMonths();

            private List<DateTime> GetActiveMonths()
            {
                var months = new List<DateTime>();
                var start = new DateTime(StartDate.Year, StartDate.Month, 1);
                var end = new DateTime(EndDate.Year, EndDate.Month, 1);

                for (var date = start; date <= end; date = date.AddMonths(1))
                {
                    months.Add(date);
                }
                return months;
            }

        }

        public class PersonnelEffort
        {
            public int EffortId { get; set; }
            public int PersonId { get; set; }
            public string PersonName { get; set; }

            public Dictionary<DateTime, decimal> Efforts { get; set; } = new Dictionary<DateTime, decimal>();
            
        }

        public class PersonnelInfo
        {
            public int PersonId { get; set; }
            public string PersonName { get; set; }

            public Dictionary<DateTime, decimal> TotalEffortMonth { get; set; } = new Dictionary<DateTime, decimal>();

            public Dictionary<DateTime, decimal> MaxEffortMonth { get; set; } = new Dictionary<DateTime, decimal>();
            public List<MonthStatus> MonthlyStatuses { get; set; } = new List<MonthStatus>();
        }
        

    }

}



