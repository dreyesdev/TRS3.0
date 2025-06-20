namespace TRS2._0.Models.ViewModels
{
    public class LeaderGlobalEffortViewModel
    {
        public int Year { get; set; }
        public List<LeaderGlobalEffortPersonViewModel> People { get; set; } = new();
    }

    public class LeaderGlobalEffortPersonViewModel
    {
        public int PersonId { get; set; }
        public string FullName { get; set; }

        // Clave = mes (1 a 12), valor = (asignado, máximo permitido)
        public Dictionary<int, (decimal Assigned, decimal Max)> MonthlyEffort { get; set; } = new();
    }
}
