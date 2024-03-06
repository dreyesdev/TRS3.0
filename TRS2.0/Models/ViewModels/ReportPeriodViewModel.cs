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


    }
}
