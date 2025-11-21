using TRS2._0.Models.DataModels;

namespace TRS2._0.Models.ViewModels
{
    /// <summary>
    /// Vista envoltorio para los Rates de un periodo concreto de un proyecto.
    /// Se usa para pintar los tabs (Estimado / Timesheet) y cargar por AJAX la malla.
    /// </summary>
    public class PeriodRatesWrapperViewModel
    {
        public ReportPeriod ReportPeriod { get; set; }
        public int ProjectId { get; set; }
    }

    /// <summary>
    /// ViewModel de la malla de Rates para un periodo.
    /// La misma malla sirve tanto para el modo Estimado como para el modo Timesheet.
    /// </summary>
    public class PeriodRatesGridViewModel
    {
        public ReportPeriod ReportPeriod { get; set; }
        public int ProjectId { get; set; }

        /// <summary>
        /// Modo de cálculo: "Estimated" (Fase 2) o "Timesheet" (Fase 3).
        /// </summary>
        public string Mode { get; set; } = "Estimated";

        /// <summary>
        /// Personas del proyecto que se mostrarán en la malla.
        /// </summary>
        public List<Personnel> Persons { get; set; } = new();

        /// <summary>
        /// Lista de meses (primer día del mes) dentro del periodo de reporte.
        /// </summary>
        public List<DateTime> Months { get; set; } = new();

        /// <summary>
        /// Coste mensual por persona y mes.
        /// Clave externa: PersonId.
        /// Clave interna: primer día del mes.
        /// Valor: coste del mes según el modo seleccionado.
        /// </summary>
        public Dictionary<int, Dictionary<DateTime, decimal>> MonthlyCostsByPerson { get; set; }
            = new();

        public Dictionary<int, decimal> TotalByPerson { get; set; } = new();
        public Dictionary<DateTime, decimal> TotalByMonth { get; set; } = new();
        public decimal GrandTotal { get; set; }
    }
}
