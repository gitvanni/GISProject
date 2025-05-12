using NetTopologySuite.Geometries;

namespace GISProject.Models
{

    public class User
    {
        public long Id { get; set; }

        public string Email { get; set; } = string.Empty;

        public string Name { get; set; }


        public string Surname { get; set; }

        public string PasswordHash { get; set; }

        public Point? CurrentLocation { get; set; }

        public ICollection<Trail> CreatedTrails { get; set; }

    }

}
