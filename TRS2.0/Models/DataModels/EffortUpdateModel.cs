namespace TRS2._0.Models.DataModels
{
    public class EffortUpdateModel
    {
        public class EffortData
        {
            public int PersonId { get; set; }
            public int WpId { get; set; }
            public DateTime Month { get; set; }
            public decimal Effort { get; set; }
        }

        public List<EffortData> Efforts { get; set; }        

        public int ProjectId { get; set; }

    }
        

}