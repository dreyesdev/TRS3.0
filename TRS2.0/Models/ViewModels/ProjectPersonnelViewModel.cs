using TRS2._0.Models.DataModels;

namespace TRS2._0.Models.ViewModels
{
    public class ProjectPersonnelViewModel
    {

        public Project ProjectDetails { get; set; }
        public IEnumerable<Personnel> AllPersonnel { get; set; }

        public IEnumerable<Wp> WorkPackages { get; set; }
        public IEnumerable<Projectxperson> ProjectPersonnel { get; set; }
        public IEnumerable<Wpxperson> WorkPackagePersonnel { get; set; }
    }


}
