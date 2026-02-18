namespace TRS2._0.Models.ViewModels
{
    public class OutOfContractAssignedEffortPageViewModel
    {
        public List<Row> Rows { get; set; } = new();

        public class Row
        {
            public int PersonId { get; set; }
            public string PersonFullName { get; set; } = string.Empty;
            public int ProjectId { get; set; }
            public string ProjectDisplayName { get; set; } = string.Empty;
            public DateTime Month { get; set; }
            public decimal AssignedEffort { get; set; }
            public string ResolveUrl { get; set; } = string.Empty;
        }
    }
}
