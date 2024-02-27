using System.ComponentModel.DataAnnotations.Schema;
using System.ComponentModel.DataAnnotations;

namespace TRS2._0.Models.DataModels
{
    [Table("AffHours")]
    public class AffHours
    {
        [Key]
        public int Id { get; set; }

        [ForeignKey("Affiliation")]
        public int AffId { get; set; }

        [Column(TypeName = "datetime2")]
        public DateTime StartDate { get; set; }

        [Column(TypeName = "datetime2")]
        public DateTime EndDate { get; set; }

        [Column(TypeName = "decimal(5, 2)")]
        public decimal Hours { get; set; }

        public virtual Affiliation Affiliation { get; set; }
    }
}
