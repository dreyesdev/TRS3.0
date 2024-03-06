using TRS2._0.Models.DataModels;

namespace TRS2._0.Models.ViewModels
{
    public class PeriodDetailsViewModel
    {
        public ReportPeriod ReportPeriod { get; set; }
        public List<Wp> WorkPackages { get; set; }
        public List<Personnel> Persons { get; set; }
        public List<string> Months { get; set; } = new List<string>();

        public void CalculateMonths(DateTime startDate, DateTime endDate)
        {
            var totalMonths = ((endDate.Year - startDate.Year) * 12) + endDate.Month - startDate.Month + 1;
            for (int i = 0; i < totalMonths; i++)
            {
                var month = startDate.AddMonths(i);
                Months.Add($"M{i + 1} | {month.ToString("MMM dd")}");
            }
        }
    }
}
