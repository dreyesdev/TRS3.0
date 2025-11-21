using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace TRS2._0.Models.DataModels;

[Table("dedication")]
public partial class Dedication
{
    [Key]
    public int Id { get; set; } // Clave primaria autoincrementable
    public int PersId { get; set; }
    [Column(TypeName = "decimal(5, 4)")]
    public decimal Reduc { get; set; }
    [Column(TypeName = "date")]
    public DateTime Start { get; set; }
    [Column(TypeName = "date")]
    public DateTime End { get; set; }
    public int Type { get; set; }
    [ForeignKey("PersId")]
    public virtual Personnel Pers { get; set; } = null!;

    public int? LineId { get; set; }
    public bool Exist { get; set; }

    // Coste anual de esa dedicación (venido del fichero DEDICACIO3)
    [Column(TypeName = "decimal(18, 4)")]
    public decimal AnnualCost { get; set; } 

}

