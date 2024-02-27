using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace TRS2._0.Models.DataModels;

[Table("projectxperson")]
[Index("ProjId", Name = "pxp1")]
[Index("Person", Name = "pxp2")]
public partial class Projectxperson
{
    [Key]
    public int Id { get; set; }

    public int ProjId { get; set; }

    public int Person { get; set; }

    [ForeignKey("Person")]
    [InverseProperty("Projectxpeople")]
    public virtual Personnel PersonNavigation { get; set; } = null!;

    [ForeignKey("ProjId")]
    [InverseProperty("Projectxpeople")]
    public virtual Project Proj { get; set; } = null!;
}
