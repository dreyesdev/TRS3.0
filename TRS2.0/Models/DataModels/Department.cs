using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace TRS2._0.Models.DataModels;

[Table("department")]
public partial class Department
{
    [Key]
    public int Id { get; set; }

    [StringLength(50)]
    public string? Name { get; set; }

    [InverseProperty("DepartmentNavigation")]
    public virtual ICollection<Personnel> Personnel { get; set; } = new List<Personnel>();
}
