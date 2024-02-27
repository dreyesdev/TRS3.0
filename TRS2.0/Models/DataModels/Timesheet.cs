using System.ComponentModel.DataAnnotations.Schema;
using System.ComponentModel.DataAnnotations;

namespace TRS2._0.Models.DataModels
{
    [Table("Timesheets")]
    public class Timesheet
    {
        [Key, Column(Order = 0)]
        public int WpxPersonId { get; set; }

        [Key, Column(Order = 1)]
        public DateTime Day { get; set; }

        public decimal Hours { get; set; }

        [ForeignKey("WpxPersonId")]
        public virtual Wpxperson WpxPersonNavigation { get; set; }
    }
}
