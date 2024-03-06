using System.ComponentModel.DataAnnotations.Schema;
using System.ComponentModel.DataAnnotations;

namespace TRS2._0.Models.DataModels
{
    [Table("ReportPeriods")]
    public class ReportPeriod
    {
        [Key]
        public int Id { get; set; }

        [Column(TypeName = "date")]
        public DateTime StartDate { get; set; }

        [Column(TypeName = "date")]
        public DateTime EndDate { get; set; }

        // Clave foránea de Project
        public int ProjId { get; set; }

        // Propiedad de navegación
        [ForeignKey("ProjId")]
        public virtual Project Project { get; set; }
    }
}
