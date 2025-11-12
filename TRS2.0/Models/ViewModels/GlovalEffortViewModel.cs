namespace TRS2._0.Models.ViewModels
{
    public class GlobalEffortEntry
    {
        public int PersonId { get; set; }
        public string PersonName { get; set; }
        public string Department { get; set; }
        public string Group { get; set; }
        public Dictionary<string, string> MonthlyEffortSummary { get; set; } // "asignado | máximo"
    }

    public class GlobalEffortViewModel
    {
        public List<GlobalEffortEntry> Entries { get; set; }
    }

}
