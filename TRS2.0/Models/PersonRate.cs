using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TRS2._0.Models.DataModels
{
    [Table("PersonRates")]
    public class PersonRate
    {
        [Key]
        public int Id { get; set; }

        // Id de la persona en TRS (tabla personnel)
        [ForeignKey(nameof(Personnel))]
        public int PersonId { get; set; }

        public virtual Personnel Personnel { get; set; }

        // Afiliación utilizada para calcular este tramo de rate
        [ForeignKey(nameof(Affiliation))]
        public int AffId { get; set; }

        public virtual Affiliation Affiliation { get; set; }

        // Rango temporal en el que este rate es válido
        [Column(TypeName = "date")]
        public DateTime StartDate { get; set; }

        [Column(TypeName = "date")]
        public DateTime EndDate { get; set; }

        // Coste anual procedente del fichero DEDICACIO3
        [Column(TypeName = "decimal(18,2)")]
        public decimal AnnualCost { get; set; }

        // Dedicación como fracción (0.7 = 70%, 1.0 = 100%, etc.)
        [Column(TypeName = "decimal(6,4)")]
        public decimal Dedication { get; set; }

        // Horas anuales de la afiliación en ese periodo
        [Column(TypeName = "decimal(9,2)")]
        public decimal AnnualHours { get; set; }

        // Resultado del cálculo: coste por hora
        // Fórmula: AnnualCost / (Dedication * AnnualHours)
        [Column(TypeName = "decimal(18,4)")]
        public decimal HourlyRate { get; set; }

        // Marca temporal de creación del registro (útil para debug / auditoría)
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
