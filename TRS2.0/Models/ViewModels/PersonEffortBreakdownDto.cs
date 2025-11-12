using System;
using System.Collections.Generic;

namespace TRS2._0.Models.ViewModels
{
    public class MonthEffortItem
    {
        public string ProjectAcronym { get; set; } = "";
        public string WpName { get; set; } = "";
        public decimal Value { get; set; }
    }

    public class PersonEffortBreakdownDto
    {
        public int PersonId { get; set; }
        public int Year { get; set; }
        // Clave = 1..12, Valor = lista de efforts en ese mes
        public Dictionary<int, List<MonthEffortItem>> ByMonth { get; set; } = new();
    }
}
