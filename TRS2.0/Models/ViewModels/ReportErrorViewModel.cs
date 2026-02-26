using Microsoft.AspNetCore.Http;
using System.ComponentModel.DataAnnotations;

namespace TRS2._0.Models.ViewModels
{
    public class ReportErrorViewModel
    {
        [Display(Name = "User")]
        public string? ReporterUserName { get; set; }

        [Display(Name = "Full name")]
        public string? ReporterFullName { get; set; }

        [Display(Name = "Email")]
        public string? ReporterEmail { get; set; }

        [Required(ErrorMessage = "Title is required.")]
        [Display(Name = "Error title")]
        public string Title { get; set; }

        [Required(ErrorMessage = "Description is required.")]
        [Display(Name = "Error description")]
        [DataType(DataType.MultilineText)]
        public string Description { get; set; }

        [Display(Name = "Attachment")]
        public IFormFile? Attachment { get; set; } // ✅ Permitir nulo para evitar que IsValid falle
    }
}
