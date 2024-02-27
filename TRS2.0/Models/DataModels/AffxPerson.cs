using System.ComponentModel.DataAnnotations.Schema;
using System.ComponentModel.DataAnnotations;

namespace TRS2._0.Models.DataModels
{
    public class AffxPerson
    {
        [Key]
        public int Id { get; set; }

        [ForeignKey("Personnel")]
        public int PersonId { get; set; }
        public virtual Personnel Personnel { get; set; }

        [ForeignKey("Affiliation")]
        public int AffId { get; set; }
        public virtual Affiliation Affiliation { get; set; }

        [DataType(DataType.Date)]
        public DateTime Start { get; set; }

        [DataType(DataType.Date)]
        public DateTime End { get; set; }
    }
}
