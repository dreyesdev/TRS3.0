using System.Globalization;
using TRS2._0.Models.DataModels;

namespace TRS2._0.Models.ViewModels
{
    public class PeriodDetailsViewModel
    {
        public ReportPeriod ReportPeriod { get; set; }
        public List<Wp> WorkPackages { get; set; }
        public List<PersonnelDetails> Persons { get; set; }
        public Dictionary<string, string> MonthDetails { get; set; } = new Dictionary<string, string>();

        public void CalculateMonths(DateTime startDate, DateTime endDate)
        {
            int totalMonths = ((endDate.Year - startDate.Year) * 12) + endDate.Month - startDate.Month + 1;
            for (int i = 0; i < totalMonths; i++)
            {
                var monthDate = startDate.AddMonths(i);
                string monthIndex = $"M{i + 1}";
                string monthName = monthDate.ToString("MMM yyyy", CultureInfo.InvariantCulture); // Formato del nombre del mes y año

                MonthDetails.Add(monthIndex, monthName); // Añade el par clave-valor al diccionario
            }
        }

        public class PersonnelDetails
        {
            public Personnel Personnel { get; set; }
            
            public Dictionary<DateTime, decimal> TotalEffortinProyect { get; set; }

            public Dictionary<DateTime, decimal> DeclaredHours { get; set; }

            public Dictionary<DateTime, decimal> HoursinProyect { get; set; }

            public Dictionary<DateTime, decimal> TotalHours { get; set; }

            // Diccionario que mapea ID de persona a un diccionario de año-mes a un par de bools (Out of Contract, Overloaded)
            public Dictionary<string, (bool OutOfContract, bool Overloaded)> PersonStatusByMonth {get; set;}

            public Dictionary<string, bool> LockStatusByMonth { get; set; }

            public Dictionary<DateTime, decimal> CompletionPercentage { get; set; }

        }
    }
}
