using TRS2._0.Models.DataModels;
using TRS2._0.Services;

namespace TRS2._0.Models.ViewModels
{
    public class EffortPlanViewModel
    {
        public int ProjId { get; set; }
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public List<WorkPackageEffort> WorkPackages { get; set; }   
        public EffortPlanViewModel()
        {
            WorkPackages = new List<WorkPackageEffort>();
        }

        // Método para obtener la lista de meses en el rango
        public List<DateTime> GetMonthsInRange()
        {
            var months = new List<DateTime>();
            DateTime month = new DateTime(StartDate.Year, StartDate.Month, 1);

            while (month <= EndDate)
            {
                months.Add(month);
                month = month.AddMonths(1);
            }

            return months;
        }
    }



    public class WorkPackageEffort
    {
        public string WpName { get; set; } // Nombre del WP
        public int WpId { get; set; } // ID del WP, útil para guardar los cambios
        public Dictionary<DateTime, float> MonthlyEfforts { get; set; } // Esfuerzos por mes para este WP
        public float TotalEffort { get; set; } // Total acumulado de esfuerzos para este WP

        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }


        public WorkPackageEffort()
        {
            MonthlyEfforts = new Dictionary<DateTime, float>();
        }


        public bool IsValidMonth(DateTime month)
        {
            // Aquí se asume que 'month' es el primer día del mes en cuestión y 
            // 'StartDate' y 'EndDate' son las fechas de inicio y fin del WP.
            // Ajusta el método 'AddMonths' y 'AddDays' según la lógica de tu aplicación.
            DateTime monthStart = new DateTime(month.Year, month.Month, 1);
            DateTime monthEnd = monthStart.AddMonths(1).AddDays(-1);

            return StartDate <= monthEnd && EndDate >= monthStart;
        }

        public float getPMs(int WpId)
        {   
                    float PMs = 0;
                   using (var context = new TRSDBContext())
            {
                var wp = context.Wps.FirstOrDefault(w => w.Id == WpId);
                PMs = wp.Pms;
            }
            return PMs;
        }

    }

}
