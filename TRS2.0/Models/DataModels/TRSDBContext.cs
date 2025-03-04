using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using TRS2._0.Models.DataModels.TRS2._0.Models.DataModels;

namespace TRS2._0.Models.DataModels;

public partial class TRSDBContext : IdentityDbContext<ApplicationUser>
{
    public TRSDBContext()
    {
    }

    public TRSDBContext(DbContextOptions<TRSDBContext> options)
        : base(options)
    {
    }

    public virtual DbSet<Dedication> Dedications { get; set; }

    public virtual DbSet<Department> Departments { get; set; }

    public virtual DbSet<Perseffort> Persefforts { get; set; }

    public virtual DbSet<Personnel> Personnel { get; set; }

    public virtual DbSet<Personnelgroup> Personnelgroups { get; set; }

    public virtual DbSet<Project> Projects { get; set; }

    public virtual DbSet<Projectxperson> Projectxpeople { get; set; }

    public virtual DbSet<Projeffort> Projefforts { get; set; }

    public virtual DbSet<Wp> Wps { get; set; }

    public virtual DbSet<Wpxperson> Wpxpeople { get; set; }

    public virtual DbSet<Leave> Leaves { get; set; }

    public DbSet<NationalHoliday> NationalHolidays { get; set; }
    public DbSet<DailyPMValue> DailyPMValues { get; set; }

    public DbSet<PersMonthEffort> PersMonthEfforts { get; set; }

    public DbSet<Liquidation> Liquidations { get; set; }

    public DbSet<Liqdayxproject> liqdayxproject { get; set; }

    public DbSet<Timesheet> Timesheets { get; set; }

    public DbSet<Affiliation> Affiliations { get; set; }

    public DbSet<AffxPerson> AffxPersons { get; set; }

    public DbSet<AffHours> AffHours { get; set; }

    public DbSet<ReportPeriod> ReportPeriods { get; set; }

    public DbSet<ProjectMonthLock> ProjectMonthLocks { get; set; }

    public DbSet<AffCodification> AffCodifications { get; set; }

    public DbSet<Leader> Leaders { get; set; }

    public DbSet<AgreementEvent> AgreementEvents { get; set; }

    public DbSet<AffGlobalHours> AffGlobalHours { get; set; }

    public DbSet<TimesheetErrorLog> TimesheetErrorLogs { get; set; }

    public DbSet<ProcessExecutionLog> ProcessExecutionLogs { get; set; }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
#warning To protect potentially sensitive information in your connection string, you should move it out of source code. You can avoid scaffolding the connection string by using the Name= syntax to read it from configuration - see https://go.microsoft.com/fwlink/?linkid=2131148. For more guidance on storing connection strings, see http://go.microsoft.com/fwlink/?LinkId=723263.
        => optionsBuilder.UseSqlServer("data source = OPSTRS03.BSC.ES; initial catalog = TRSBDD; user id = admin; password = seidor; Trusted_Connection = True;TrustServerCertificate=True;Integrated Security=False;");

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Dedication>(entity =>
        {
            entity.HasOne(d => d.Pers).WithMany().HasConstraintName("dedication$deidcationx1");
        });

        modelBuilder.Entity<Department>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK_department_Id");

            entity.Property(e => e.Id).ValueGeneratedNever();
        });

        modelBuilder.Entity<Perseffort>(entity =>
        {
            entity.HasKey(e => e.Code).HasName("PK_perseffort_Code");

            entity.HasOne(d => d.WpxPersonNavigation).WithMany(p => p.Persefforts).HasConstraintName("perseffort$X4");
        });

        modelBuilder.Entity<Personnel>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK_personnel_Id");

            entity.Property(e => e.Id).ValueGeneratedNever();

            entity.HasOne(d => d.DepartmentNavigation).WithMany(p => p.Personnel)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("personnel$X1");
            entity.Property(e => e.Password).HasMaxLength(255).IsRequired(false);
            entity.Property(e => e.PermissionLevel).HasMaxLength(50).IsRequired(false).HasDefaultValue("User");
        });

        modelBuilder.Entity<Personnelgroup>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK_personnelgroup_Id");

            entity.Property(e => e.Id).ValueGeneratedNever();
        });

        modelBuilder.Entity<Project>(entity =>
        {
            entity.HasKey(e => e.ProjId).HasName("PK_projects_ProjID");
        });

        modelBuilder.Entity<Projectxperson>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK_projectxperson_Id");

            entity.HasOne(d => d.PersonNavigation).WithMany(p => p.Projectxpeople).HasConstraintName("projectxperson$pxp2");

            entity.HasOne(d => d.Proj).WithMany(p => p.Projectxpeople).HasConstraintName("projectxperson$pxp1");
        });

        modelBuilder.Entity<Projeffort>(entity =>
        {
            entity.HasOne(d => d.WpNavigation).WithMany().HasConstraintName("projeffort$X5");
        });

        modelBuilder.Entity<Wp>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK_wp_Id");

            entity.HasOne(d => d.Proj).WithMany(p => p.Wps).HasConstraintName("wp$wp_ibfk_1");
        });

        modelBuilder.Entity<Wpxperson>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK_wpxperson_Id");

            entity.HasOne(d => d.PersonNavigation).WithMany(p => p.Wpxpeople).HasConstraintName("wpxperson$wxp2");

            entity.HasOne(d => d.WpNavigation).WithMany(p => p.Wpxpeople).HasConstraintName("wpxperson$wxp1");
        });

        modelBuilder.Entity<Leave>(entity =>
        {
            // Definir la clave primaria compuesta
            entity.HasKey(e => new { e.PersonId, e.Day });

            // Configurar la relación con Personnel
            entity.HasOne(d => d.PersonNavigation)
                  .WithMany(p => p.Leaves) // Asegúrate de que Personnel tiene una propiedad ICollection<Leave> Leaves
                  .HasForeignKey(d => d.PersonId)
                  .HasConstraintName("FK_Leave_Personnel");
        });

        // Configurar clave compuesta para Projeffort
        modelBuilder.Entity<Projeffort>()
            .HasKey(e => new { e.Wp, e.Month });

        modelBuilder.Entity<Dedication>(entity =>
        {
            entity.HasKey(e => e.Id); // Establece Id como clave primaria
            entity.HasOne(d => d.Pers).WithMany().HasForeignKey(d => d.PersId).HasConstraintName("dedication$deidcationx1");
        });

        modelBuilder.Entity<Liquidation>()
    .HasOne(l => l.Personnel)
    .WithMany(p => p.Liquidations) // Asume que Personnel tiene una propiedad ICollection<Liquidation> Liquidations
    .HasForeignKey(l => l.PersId);


        modelBuilder.Entity<PersMonthEffort>()
            .HasKey(e => new { e.PersonId, e.Month });

        modelBuilder.Entity<Liqdayxproject>().HasKey(l => new { l.LiqId, l.ProjId, l.Day });

        // Configurar la relación y especificar NO ACTION para las operaciones de DELETE
        modelBuilder.Entity<Liqdayxproject>()
            .HasOne(l => l.Personnel)
            .WithMany(l => l.Liqdayxprojects) // Si Personnel tiene una propiedad de navegación hacia Liqdayxprojects, especifícala aquí
            .HasForeignKey(l => l.PersId)
            .OnDelete(DeleteBehavior.NoAction); // Esto previene la eliminación en cascada

        modelBuilder.Entity<Liqdayxproject>()
            .HasOne(l => l.Project)
            .WithMany(l => l.Liqdayxprojects) // Si Project tiene una propiedad de navegación hacia Liqdayxprojects, especifícala aquí
            .HasForeignKey(l => l.ProjId)
            .OnDelete(DeleteBehavior.NoAction); // Esto previene la eliminación en cascada

        modelBuilder.Entity<Liqdayxproject>()
            .HasOne(l => l.Liquidation)
            .WithMany(l => l.Liqdayxprojects) // Asume que Liquidation tiene una propiedad de navegación hacia Liqdayxprojects, especifícala aquí
            .HasForeignKey(l => l.LiqId)
            .OnDelete(DeleteBehavior.NoAction); // Esto previene la eliminación en cascada

        // Configuración para Timesheet
        modelBuilder.Entity<Timesheet>().HasKey(t => new { t.WpxPersonId, t.Day }); // Clave compuesta
       


        modelBuilder.Entity<Timesheet>()
            .HasOne(t => t.WpxPersonNavigation)
            .WithMany(p => p.Timesheets) 
            .HasForeignKey(t => t.WpxPersonId);

        
        modelBuilder.Entity<Affiliation>().HasKey(a => a.Id); // Clave primaria
        // Configura la relación uno-a-muchos entre Personnel y AffxPerson
        modelBuilder.Entity<AffxPerson>()
            .HasOne(p => p.Personnel) // Un AffxPerson tiene un Personnel
            .WithMany(b => b.AffxPersons) // Un Personnel tiene muchos AffxPersons
            .HasForeignKey(p => p.PersonId); // La clave foránea en AffxPerson que apunta a Personnel

        // Configura la relación uno-a-muchos entre Affiliation y AffxPerson
        modelBuilder.Entity<AffxPerson>()
            .HasOne(a => a.Affiliation) // Un AffxPerson tiene una Affiliation
            .WithMany() // Una Affiliation tiene muchos AffxPersons, si no tienes una propiedad de navegación en Affiliation, puedes dejar este lado sin especificar.
            .HasForeignKey(a => a.AffId); // La clave foránea en AffxPerson que apunta a Affiliation

        // Configura la entidad AffHours
        modelBuilder.Entity<AffHours>(entity =>
        {
            entity.HasKey(e => e.Id); // Clave primaria

            entity.Property(e => e.Hours)
                .HasColumnType("decimal(5, 2)"); // Configura la precisión y escala de Hours

            // Configura la relación uno-a-muchos entre Affiliation y AffHours
            entity.HasOne(d => d.Affiliation) // Un AffHours tiene una Affiliation
                .WithMany() // Una Affiliation tiene muchos AffHours, si no tienes una propiedad de navegación en Affiliation, puedes dejar este lado sin especificar.
                .HasForeignKey(d => d.AffId) // La clave foránea en AffHours que apunta a Affiliation
                .OnDelete(DeleteBehavior.ClientSetNull); // Configura el comportamiento en caso de eliminación
        });
        OnModelCreatingPartial(modelBuilder);

        modelBuilder.Entity<AffCodification>()
            .HasKey(c => new { c.Contract, c.Dist });

        modelBuilder.Entity<Personnel>()
                .HasOne(p => p.ApplicationUser)
                .WithOne(u => u.Personnel)
                .HasForeignKey<ApplicationUser>(u => u.PersonnelId);

        modelBuilder.Entity<AgreementEvent>()
        .ToTable("AgreementEvents") // Asegúrate de que el nombre coincida exactamente
        .Property(a => a.AgreementEventId);
        
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}
