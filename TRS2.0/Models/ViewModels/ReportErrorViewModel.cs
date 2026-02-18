using Microsoft.AspNetCore.Http;
using System.ComponentModel.DataAnnotations;

namespace TRS2._0.Models.ViewModels
{
    public class ReportErrorViewModel
    {
        [Display(Name = "Usuario")]
        public string? ReporterUserName { get; set; }

        [Display(Name = "Nombre")]
        public string? ReporterFullName { get; set; }

        [Display(Name = "Correo")]
        public string? ReporterEmail { get; set; }

        [Required(ErrorMessage = "El título es obligatorio.")]
        [Display(Name = "Título del Error")]
        public string Title { get; set; }

        [Required(ErrorMessage = "La descripción es obligatoria.")]
        [Display(Name = "Descripción del Error")]
        [DataType(DataType.MultilineText)]
        public string Description { get; set; }

        [Display(Name = "Adjuntar Archivo")]
        public IFormFile? Attachment { get; set; } // ✅ Permitir nulo para evitar que IsValid falle
    }
}
