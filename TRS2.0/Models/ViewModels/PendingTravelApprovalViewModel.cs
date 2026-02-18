namespace TRS2._0.Models.ViewModels
{
    public class PendingTravelApprovalViewModel
    {
        public string Code { get; set; } = string.Empty;
        public int PersonId { get; set; }
        public string PersonName { get; set; } = string.Empty;
        public string StartDate { get; set; } = string.Empty;
        public string EndDate { get; set; } = string.Empty;
        public string Project1 { get; set; } = "N/A";
        public decimal Dedication1 { get; set; }
        public string Project2 { get; set; } = "N/A";
        public decimal Dedication2 { get; set; }
        public string Destiny { get; set; } = string.Empty;
        public string Status { get; set; } = "Pending";
    }
}
