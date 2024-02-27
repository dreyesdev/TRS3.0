using System.ComponentModel.DataAnnotations.Schema;
using System.ComponentModel.DataAnnotations;

namespace TRS2._0.Models.DataModels
{
    [Table("Liqdayxproject")]
    public class Liqdayxproject
    {
        [Key, Column(Order = 0)]
        public string LiqId { get; set; }
        
        public int PersId { get; set; }

        [Key, Column(Order = 1)]
        public int ProjId { get; set; }

        [Key]
        [Column(Order = 2)]
        [DataType(DataType.Date)]
        public DateTime Day { get; set; }

        [Column(TypeName = "decimal(5, 2)")]
        public decimal Dedication { get; set; }

        [Column(TypeName = "decimal(5, 2)")]
        public decimal PMs { get; set; }

        public string Status { get; set; }

        [ForeignKey("PersId")]
        public virtual Personnel Personnel { get; set; }

        [ForeignKey("ProjId")]
        public virtual Project Project { get; set; }

        [ForeignKey("LiqId")]
        public virtual Liquidation Liquidation { get; set; }

        
    }
}
