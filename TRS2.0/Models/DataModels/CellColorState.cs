namespace TRS2._0.Models.DataModels
{
    public class CellColorState
    {
        public string Color { get; set; }
        public string Gradient { get; set; }
        public bool IsOutOfContract { get; set; }
        public bool IsOverloaded { get; set; }
        public List<int> LeaveTypes { get; set; } = new List<int>();

        public CellColorState()
        {
            Color = "white"; // Color por defecto
            Gradient = string.Empty;
            IsOutOfContract = false;
            IsOverloaded = false;
        }
    }
}
