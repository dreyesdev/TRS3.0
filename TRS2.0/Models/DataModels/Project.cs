using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace TRS2._0.Models.DataModels;

[Table("projects")]
[Index("SapCode", Name = "projects$ProjConstr", IsUnique = true)]
public partial class Project
{
    [Key]
    [Column("ProjID")]
    public int ProjId { get; set; }

    [StringLength(50)]
    public string? SapCode { get; set; }

    [StringLength(50)]
    public string? Acronim { get; set; }

    [StringLength(250)]
    public string? Title { get; set; }

    [StringLength(50)]
    public string? Contract { get; set; }

    [Column(TypeName = "date")]
    public DateTime? Start { get; set; }

    [Column(TypeName = "date")]
    public DateTime? End { get; set; }

    [Column("TPsUPC")]
    public short? TpsUpc { get; set; }

    [Column("TPsICREA")]
    public short? TpsIcrea { get; set; }

    [Column("TPsCSIC")]
    public short? TpsCsic { get; set; }

    [Column("PI")]
    public int? Pi { get; set; }

    [Column("PM")]
    public int? Pm { get; set; }

    [StringLength(50)]
    public string Type { get; set; } = null!;

    [Column("sType")]
    [StringLength(50)]
    public string SType { get; set; } = null!;

    [StringLength(50)]
    public string St1 { get; set; } = null!;

    [StringLength(50)]
    public string St2 { get; set; } = null!;

    [Column(TypeName = "date")]
    public DateTime EndReportDate { get; set; }

    public short Visible { get; set; }

    [InverseProperty("Proj")]
    public virtual ICollection<Projectxperson> Projectxpeople { get; set; } = new List<Projectxperson>();

    [InverseProperty("Proj")]
    public virtual ICollection<Wp> Wps { get; set; } = new List<Wp>();

    [InverseProperty("Project")]
    public virtual ICollection<Liqdayxproject> Liqdayxprojects { get; set; } = new List<Liqdayxproject>();
}
