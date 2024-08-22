using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using TRS2._0.Models.DataModels.TRS2._0.Models.DataModels;

namespace TRS2._0.Models.DataModels
{
    [Table("personnel")]
    [Index("Department", Name = "X1")]
    [Index("Affiliation", Name = "X2")]
    [Index("Id", "BscId", "Name", Name = "personnel$PersConstr", IsUnique = true)]
    public partial class Personnel
    {
        [Key]
        public int Id { get; set; }

        [StringLength(50)]
        public string? BscId { get; set; }

        [StringLength(50)]
        public string? Name { get; set; }

        [StringLength(50)]
        public string? Surname { get; set; }

        public int? Department { get; set; }

        public int? Affiliation { get; set; }

        [Column(TypeName = "date")]
        public DateTime? StartDate { get; set; }

        [Column(TypeName = "date")]
        public DateTime? EndDate { get; set; }

        [StringLength(50)]
        public string? Category { get; set; }

        public int Resp { get; set; }

        public int? PersonnelGroup { get; set; }

        [Column("email")]
        [StringLength(50)]
        public string Email { get; set; } = null!;

        [Column("A3Code")]
        public int? A3code { get; set; }

        [StringLength(50)]
        public string Password { get; set; } = null!;

        public string? PermissionLevel { get; set; }

        public int? UserId { get; set; }

        public virtual ApplicationUser ApplicationUser { get; set; }

        [ForeignKey("Department")]
        [InverseProperty("Personnel")]
        public virtual Department? DepartmentNavigation { get; set; }

        [InverseProperty("PersonNavigation")]
        public virtual ICollection<Projectxperson> Projectxpeople { get; set; } = new List<Projectxperson>();

        [InverseProperty("PersonNavigation")]
        public virtual ICollection<Wpxperson> Wpxpeople { get; set; } = new List<Wpxperson>();

        public virtual ICollection<Leave> Leaves { get; set; } = new List<Leave>();

        public virtual ICollection<Liqdayxproject> Liqdayxprojects { get; set; } = new List<Liqdayxproject>();
        public virtual ICollection<Liquidation> Liquidations { get; set; } = new List<Liquidation>();

        [InverseProperty("Personnel")]
        public virtual ICollection<AffxPerson> AffxPersons { get; set; } = new List<AffxPerson>();
    }
}