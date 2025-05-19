using NetTopologySuite.Geometries;
using GISProject.Enum;

namespace GISProject.Models
{
    public class PointOfInterest
    {
        public long Id { get; set; }

        public string Name { get; set; } = string.Empty;

        public PoiCategory Category { get; set; } = PoiCategory.Generic;

        public Geometry Geometry { get; set; } = default!;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public string? Description { get; set; }

        public ICollection<Trail> Trails { get; set; }
    }

}
