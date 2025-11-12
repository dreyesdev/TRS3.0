namespace TRS2._0.Services
{
    public class ReminderEmailOptions
    {
        public string? ReplyTo { get; set; }
        public string? FromDisplayName { get; set; }
        public bool UseAssignmentCap { get; set; } = false;
    }
}
