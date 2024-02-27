using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace TRS2._0.Models.DataModels;

[Table("projeffort")]
[Index("Wp", Name = "X5")]
public partial class Projeffort
{

    [Column("WP")]
    public int Wp { get; set; }

    [Column(TypeName = "date")]
    public DateTime Month { get; set; }

    [Column(TypeName = "decimal(10, 2)")]
    public decimal Value { get; set; }

    [ForeignKey("Wp")]
    public virtual Wp WpNavigation { get; set; } = null!;
}
