namespace TRS2._0.Models.ViewModels
{
    public class LoginLogViewModel
    {
        public int Year { get; set; }
        public List<LoginLogEntry> Entries { get; set; } = new();
    }

    public class LoginLogEntry
    {
        public string PersonName { get; set; } = "";
        public string Department { get; set; } = "";
        public string Group { get; set; } = "";
        public string Email { get; set; } = "";
        // Clave = "Jan", "Feb", ... como en GlobalHours/Effort
        public Dictionary<string, string> MonthlyFirstLogin { get; set; } = new();
    }
}
