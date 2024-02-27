using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace TRS2._0.Models.DataModels
{
    [Table("Leave")]
    public class Leave
    {
        [Key, Column(Order = 0)]
        [ForeignKey("PersonNavigation")]
        public int PersonId { get; set; }

        [Key, Column("Day", Order = 1, TypeName = "date")]
        public DateTime Day { get; set; }

        public int Type { get; set; }

        public bool Legacy { get; set; }

        [Column(TypeName = "decimal(5, 2)")]
        public decimal LeaveReduction { get; set; }

        public virtual Personnel PersonNavigation { get; set; }
    }
}

