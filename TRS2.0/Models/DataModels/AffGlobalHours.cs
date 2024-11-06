using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace TRS2._0.Models.DataModels
{
    [Table("AffGlobalHours")]
    public class AffGlobalHours
    {
        [Key]
        public int Id { get; set; }

        [ForeignKey("Affiliation")]
        public int Aff { get; set; }

        public int Year { get; set; }

        public decimal Hours { get; set; }

        public virtual Affiliation Affiliation { get; set; }
    }
}
