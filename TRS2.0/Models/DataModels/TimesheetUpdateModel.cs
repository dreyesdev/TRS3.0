namespace TRS2._0.Models.DataModels
{
    public class TimesheetUpdateModel
    {

        public class TimesheetData
        {
            public int ProjectId { get; set; }
            public int WpId { get; set; }
            public int PersonId { get; set; }
            public DateTime Day { get; set; }
            public decimal Hours { get; set; }

        }

        public List<TimesheetData> TimesheetDataList { get; set; } = new List<TimesheetData>();
    }
}
