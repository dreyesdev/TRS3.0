using System.ComponentModel.DataAnnotations.Schema;
using System.ComponentModel.DataAnnotations;

namespace TRS2._0.Models.DataModels
{
    [Table("Affiliations")] // Este atributo especifica el nombre de la tabla en la base de datos
    public class Affiliation
    {
        [Key] // Indica que esta propiedad es la llave primaria de la tabla
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)] // Esto es para que el Id sea generado automáticamente por la base de datos
        public int Id { get; set; }

        [Required] // Indica que el campo es obligatorio
        [StringLength(255)] // Define la longitud máxima del campo
        public string Name { get; set; }
        
    }
}
