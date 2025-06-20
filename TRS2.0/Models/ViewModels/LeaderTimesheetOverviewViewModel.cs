namespace TRS2._0.Models.ViewModels
{
    public class LeaderTimesheetOverviewViewModel
    {
        public int Year { get; set; }
        public List<LeaderTimesheetPersonViewModel> People { get; set; } = new();
    }

    public class LeaderTimesheetPersonViewModel
    {
        public int PersonId { get; set; }
        public string FullName { get; set; }
        public Dictionary<int, (decimal Registered, decimal Max)> MonthlyHours { get; set; } = new(); // Key: mes 1-12
    }

}
