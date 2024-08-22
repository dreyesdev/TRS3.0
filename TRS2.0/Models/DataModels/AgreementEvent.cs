using System.ComponentModel.DataAnnotations;

namespace TRS2._0.Models.DataModels
{
    public class AgreementEvent
    {
        [Key]
        public int AgreementEventId { get; set; }
        public string Name { get; set; }

        public int? Type { get; set; }
    }
}
