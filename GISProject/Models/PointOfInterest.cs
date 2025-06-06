﻿using NetTopologySuite.Geometries;
using GISProject.Enumerations;

namespace GISProject.Models
{
    public class PointOfInterest
    {
        public long Id { get; set; }

        public string Name { get; set; } = string.Empty;

        public ICollection<PointOfInterestCategory> PoiCategories { get; set; }

        public Geometry Geometry { get; set; } = default!;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public string? Description { get; set; }

        public ICollection<Trail> Trails { get; set; }
    }

}
