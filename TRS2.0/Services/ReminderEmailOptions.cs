namespace TRS2._0.Services
{
    /// <summary>
    /// Configuration options applied to reminder emails.
    /// </summary>
    public class ReminderEmailOptions
    {
        /// <summary>
        /// Optional reply-to address shown to recipients.
        /// </summary>
        public string? ReplyTo { get; set; }

        /// <summary>
        /// Optional display name used as the sender name.
        /// </summary>
        public string? FromDisplayName { get; set; }

        /// <summary>
        /// When enabled, reminders use the assignment fraction as a cap for the required hours threshold.
        /// </summary>
        public bool UseAssignmentCap { get; set; }
    }
}
