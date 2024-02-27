using System.ComponentModel.DataAnnotations.Schema;
using System.ComponentModel.DataAnnotations;

namespace TRS2._0.Models.DataModels
{
    [Table("NationalHolidays")]
    public class NationalHoliday
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }

        [Required]
        [Column(TypeName = "date")]
        public DateTime Date { get; set; }

        [StringLength(100)]
        public string Description { get; set; }
    }
}
