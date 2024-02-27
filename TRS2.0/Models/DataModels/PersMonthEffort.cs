using System.ComponentModel.DataAnnotations.Schema;
using System.ComponentModel.DataAnnotations;

namespace TRS2._0.Models.DataModels
{
    public class PersMonthEffort
    {
        [Key, Column(Order = 0)]
        [DatabaseGenerated(DatabaseGeneratedOption.None)]
        public int PersonId { get; set; }

        [Key, Column(Order = 1, TypeName = "date")]
        public DateTime Month { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal Value { get; set; }

        [ForeignKey("PersonId")]
        public virtual Personnel PersonNavigation { get; set; }
    }
}
