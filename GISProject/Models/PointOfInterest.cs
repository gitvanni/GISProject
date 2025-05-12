using NetTopologySuite.Geometries;

namespace GISProject.Models
{
    public class PointOfInterest : GeoEntity
    {
        public string Name { get; set; } = string.Empty;

        public string Category { get; set; } = "generic";

        public long? TrailId { get; set; }
        public Trail? Trail { get; set; }

    }

}
