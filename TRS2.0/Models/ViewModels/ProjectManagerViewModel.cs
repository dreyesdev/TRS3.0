using System;
using System.Collections.Generic;
using TRS2._0.Models.DataModels;

namespace TRS2._0.Models.ViewModels.ProjectManager
{
    public class ProjectManagerViewModel
    {
        public List<ProcessedEntry> ProcessedEntries { get; set; } = new List<ProcessedEntry>();
        
        public List<TimesheetErrorLog> Errors { get; set; } = new List<TimesheetErrorLog>(); // Errores previos
    }

    public class ProcessedEntry
    {
        public string FileName { get; set; } // Nombre del archivo procesado
        public string PersonName { get; set; } // Persona asociada
        public string ProjectName { get; set; } // Proyecto asociado
        public string WorkPackageName { get; set; } // Paquete de trabajo
        public string Month { get; set; } // Mes procesado
        public decimal TotalHours { get; set; } // Horas totales registradas
    }

    
}

