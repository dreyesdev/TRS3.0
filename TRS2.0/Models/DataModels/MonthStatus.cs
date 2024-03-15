namespace TRS2._0.Models.DataModels
{
    public class MonthStatus
    {
        public DateTime Month { get; set; }
        public int Status { get; set; }
        public List<int> AdditionalStatuses { get; set; }

        public bool IsLocked { get; set; }
        public string Gradient { get; set; }
        // Nueva propiedad para incluir los detalles de los viajes y los proyectos asociados
        public List<TravelDetails> TravelDetails { get; set; }

        public List<int> UniqueProjIds { get; set; }
    }

}
