using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace TRS2._0.Models.DataModels;

[Table("wpxperson")]
[Index("Wp", Name = "wxp1")]
[Index("Person", Name = "wxp2")]
public partial class Wpxperson
{
    [Key]
    public int Id { get; set; }

    public int Wp { get; set; }

    public int Person { get; set; }

    [InverseProperty("WpxPersonNavigation")]
    public virtual ICollection<Perseffort> Persefforts { get; set; } = new List<Perseffort>();

    [ForeignKey("Person")]
    [InverseProperty("Wpxpeople")]
    public virtual Personnel PersonNavigation { get; set; } = null!;

    [ForeignKey("Wp")]
    [InverseProperty("Wpxpeople")]
    public virtual Wp WpNavigation { get; set; } = null!;

    [InverseProperty("WpxPersonNavigation")]
    public virtual ICollection<Timesheet> Timesheets { get; set; } = new List<Timesheet>();
}
