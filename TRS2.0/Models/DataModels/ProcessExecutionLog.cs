namespace TRS2._0.Models.DataModels
{
    public class ProcessExecutionLog
    {
        public int Id { get; set; }
        public string ProcessName { get; set; }
        public DateTime ExecutionTime { get; set; }
        public string Status { get; set; } // "Exitoso", "Advertencias", "Fallido"
        public string LogMessage { get; set; } // Mensajes de warning o error
    }
}
