namespace TRS2._0.Models
{
    public class ProjectEffortUpdateModel
    {
        public class ProjeffortUpdateData
        {
            public int WpId { get; set; }
            public string Month { get; set; }

            public decimal Value { get; set; }
        }
        
            public List<ProjeffortUpdateData> Efforts { get; set; }
        
    }
}
