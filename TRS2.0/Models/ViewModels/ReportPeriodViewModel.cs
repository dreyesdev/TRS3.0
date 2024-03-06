using TRS2._0.Models.DataModels;
using TRS2._0.Services;



namespace TRS2._0.Models.ViewModels
{
    public class ReportPeriodViewModel
    {
        public Project Project { get; set; }

        public List<ReportPeriod>? ReportPeriods { get; set; } = new List<ReportPeriod>();

        public List<Projectxperson> ProjectPersonnel { get; set; } = new List<Projectxperson>();

        public List<Wp> WorkPackages { get; set; } = new List<Wp>();

        // Añade la propiedad MonthsList
        public List<string> MonthsList { get; set; } = new List<string>();

        // Método para calcular MonthsList basado en las fechas de inicio y fin del proyecto
        public void CalculateMonthsList()
        {
            if (Project != null && Project.Start.HasValue)
            {
                // Ahora que sabemos que startProject y endProject tienen valores, procedemos con el cálculo
                DateTime startProject = Project.Start.Value;
                DateTime endProject = Project.EndReportDate;



                int monthsCount = ((endProject.Year - startProject.Year) * 12) + endProject.Month - startProject.Month + 1;

                for (int i = 0; i < monthsCount; i++)
                {
                    string monthName = startProject.AddMonths(i).ToString("MMMM yyyy", System.Globalization.CultureInfo.InvariantCulture);
                    MonthsList.Add($"M{i + 1} ({monthName})");
                }
            }
        }
    }
}
