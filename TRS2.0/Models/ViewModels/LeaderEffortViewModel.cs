using System;
using System.Collections.Generic;

namespace TRS2._0.Models.ViewModels
{
    public class LeaderEffortViewModel
    {
        public int Year { get; set; }
        public List<LeaderEffortPersonViewModel> People { get; set; }
    }

    public class LeaderEffortPersonViewModel
    {
        public int PersonId { get; set; }
        public string FullName { get; set; }
        public bool HasMultipleProjectsOrWPs { get; set; }
        public List<LeaderEffortDetail> Efforts { get; set; } = new();
    }

    public class LeaderEffortDetail
    {
        public string Project { get; set; }
        public string WP { get; set; } // si es sub-WP, este campo se usa
        public Dictionary<int, decimal> MonthlyEffort { get; set; }
        public List<LeaderEffortDetail> SubEfforts { get; set; } // si es nivel proyecto, sub-WPs
    }

}

