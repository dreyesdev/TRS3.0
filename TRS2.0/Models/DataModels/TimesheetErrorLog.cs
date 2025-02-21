namespace TRS2._0.Models.DataModels
{
    public class TimesheetErrorLog
    {
        public int Id { get; set; }
        public string FileName { get; set; }
        public string PersonName { get; set; }
        public string ProjectName { get; set; }
        public string WorkPackageName { get; set; }
        public string Month { get; set; }
        public string ErrorMessage { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.Now; // Registra la fecha del error
        public bool IsResolved { get; set; } = false; // Indica si el error fue corregido
    }
}
