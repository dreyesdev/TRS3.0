using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Rendering;
using System.Collections.Generic;
using TRS2._0.Models.DataModels;

namespace TRS2._0.Models.ViewModels
{
    public class AdminIndexViewModel
    {
        public List<DailyPMValue> DailyPMValues { get; set; }
        public List<SelectListItem> People { get; set; }
        public List<SelectListItem> Users { get; set; }
        public List<SelectListItem> Roles { get; set; }

        public List<ProcessExecutionLog> ProcessLogs { get; set; }
    }
}

