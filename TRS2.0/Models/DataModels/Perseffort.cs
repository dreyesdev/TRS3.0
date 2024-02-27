using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace TRS2._0.Models.DataModels;

[Table("perseffort")]
[Index("WpxPerson", Name = "X4")]
public partial class Perseffort
{
    [Key]
    public int Code { get; set; }

    public int WpxPerson { get; set; }

    [Column(TypeName = "date")]
    public DateTime Month { get; set; }

    [Column(TypeName = "decimal(3, 2)")]
    public decimal Value { get; set; }

    [ForeignKey("WpxPerson")]
    [InverseProperty("Persefforts")]
    public virtual Wpxperson WpxPersonNavigation { get; set; } = null!;
}
