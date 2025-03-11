using TRS2._0.Models.DataModels.TRS2._0.Models.DataModels;
using TRS2._0.Models.DataModels;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TRS2._0.Models
{
    public class UserLoginHistory
    {
        [Key]
        public int Id { get; set; }        

        [Required]
        public int PersonId { get; set; } // Nuevo campo para almacenar el ID del personal

        public DateTime LoginTime { get; set; }        

        [ForeignKey("PersonId")]
        public virtual Personnel Personnel { get; set; } // Relación con la tabla de personal
    }
}

