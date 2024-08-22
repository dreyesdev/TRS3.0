using Microsoft.AspNetCore.Identity;

namespace TRS2._0.Models.DataModels
{
    namespace TRS2._0.Models.DataModels
    {
        public class ApplicationUser : IdentityUser
        {
            public int? PersonnelId { get; set; }
            public virtual Personnel Personnel { get; set; }
        }
    }
}
