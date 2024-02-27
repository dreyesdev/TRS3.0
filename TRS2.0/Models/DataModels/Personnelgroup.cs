using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace TRS2._0.Models.DataModels;

[Table("personnelgroup")]
public partial class Personnelgroup
{
    [Key]
    public int Id { get; set; }

    [StringLength(50)]
    public string GroupName { get; set; } = null!;

    public int? Leader { get; set; }

    public int? Leader2 { get; set; }
}
