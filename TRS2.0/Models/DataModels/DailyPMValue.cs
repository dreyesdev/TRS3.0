using System.ComponentModel.DataAnnotations.Schema;
using System.ComponentModel.DataAnnotations;

namespace TRS2._0.Models.DataModels
{
    [Table("DailyPMValues")]
    public class DailyPMValue
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }

        [Required]
        public int Year { get; set; }

        [Required]
        public int Month { get; set; }

        [Required]
        public int WorkableDays { get; set; }

        [Required]
        [Column(TypeName = "decimal(18,6)")]
        public decimal PmPerDay { get; set; }
    }
}
