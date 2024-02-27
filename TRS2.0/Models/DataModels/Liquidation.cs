using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TRS2._0.Models.DataModels
{
    [Table("Liquidations")]
    public class Liquidation
    {
        [Key]
        public string Id { get; set; }

        
        public int PersId { get; set; }

        public string Project1 { get; set; }

        [Column(TypeName = "decimal(5, 2)")]
        public decimal Dedication1 { get; set; }

        public string? Project2 { get; set; }

        [Column(TypeName = "decimal(5, 2)")]
        public decimal? Dedication2 { get; set; }

        [Column(TypeName = "date")]
        public DateTime Start { get; set; }

        [Column(TypeName = "date")]
        public DateTime End { get; set; }

        public string Destiny { get; set; }

        public string Reason { get; set; }

        public string Status { get; set; }

        [ForeignKey("PersId")]
        public virtual Personnel Personnel { get; set; }

        public virtual ICollection<Liqdayxproject> Liqdayxprojects { get; set; }
    }
}
