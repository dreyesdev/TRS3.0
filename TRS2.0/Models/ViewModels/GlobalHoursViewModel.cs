namespace TRS2._0.Models.ViewModels
{
    public class GlobalHoursViewModel
    {
        public List<GlobalHoursEntry> Entries { get; set; }
        

        public class GlobalHoursEntry
        {
            public string PersonName { get; set; }
            public string Department { get; set; }
            public string Group { get; set; }
            public Dictionary<string, decimal> MonthlyHours { get; set; }
        }
    }
}
