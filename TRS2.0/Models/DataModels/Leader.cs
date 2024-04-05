using System.ComponentModel.DataAnnotations.Schema;
using System.ComponentModel.DataAnnotations;

namespace TRS2._0.Models.DataModels
{
    [Table("Leaders")]
    public class Leader
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [MaxLength(1)]
        [Column(TypeName = "char")]
        public string Tipo { get; set; }

        [Required]        
        public int GrupoDepartamento { get; set; }

        [Required]
        public int LeaderId { get; set; }
    }
}
