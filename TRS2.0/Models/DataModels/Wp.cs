using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace TRS2._0.Models.DataModels;

[Table("wp")]
[Index("ProjId", "Name", Name = "wp$ProjId", IsUnique = true)]
public partial class Wp
{
    [Key]
    public int Id { get; set; }

    public int ProjId { get; set; }

    [StringLength(100)]
    public string Name { get; set; } = null!;

    [StringLength(250)]
    public string? Title { get; set; }

    [Column(TypeName = "date")]
    public DateTime StartDate { get; set; }

    [Column(TypeName = "date")]
    public DateTime EndDate { get; set; }

    [Column("PMs")]
    public float Pms { get; set; }

    [ForeignKey("ProjId")]
    [InverseProperty("Wps")]
    public virtual Project? Proj { get; set; } = null!;

    [InverseProperty("WpNavigation")]
    public virtual ICollection<Wpxperson> Wpxpeople { get; set; } = new List<Wpxperson>();
}

public class data
{
    public int wpId { get; set; }
    public Dictionary<string, string> efforts { get; set; }
}


