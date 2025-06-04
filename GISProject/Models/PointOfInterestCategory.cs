using GISProject.Enumerations;

namespace GISProject.Models
{
    public class PointOfInterestCategory
    {
        public long PointOfInterestId { get; set; }
        public PointOfInterest PointOfInterest { get; set; } = default!;

        public PoiCategory Category { get; set; }
    }
}
