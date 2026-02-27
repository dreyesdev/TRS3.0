using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TRS2._0.Models.DataModels
{
    [Table("PersonManualRates")]
    public class PersonManualRate
    {
        [Key]
        public int Id { get; set; }

        [ForeignKey(nameof(Personnel))]
        public int PersonId { get; set; }
        public virtual Personnel Personnel { get; set; }

        [ForeignKey(nameof(Affiliation))]
        public int AffId { get; set; }
        public virtual Affiliation Affiliation { get; set; }

        [Column(TypeName = "date")]
        public DateTime StartDate { get; set; }

        [Column(TypeName = "date")]
        public DateTime EndDate { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal AnnualCost { get; set; }

        [Column(TypeName = "decimal(6,4)")]
        public decimal Dedication { get; set; }

        [Column(TypeName = "decimal(9,2)")]
        public decimal AnnualHours { get; set; }

        [Column(TypeName = "decimal(18,4)")]
        public decimal HourlyRate { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
