using Microsoft.AspNetCore.Mvc.Rendering;
using System.Collections.Generic;

namespace TRS2._0.Models.ViewModels
{
    public class ManageRolesViewModel
    {
        public List<SelectListItem> Users { get; set; }
        public List<SelectListItem> Roles { get; set; }
    }
}
