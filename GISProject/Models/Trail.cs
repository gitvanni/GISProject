using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using System;
using GISProject.Enum;

namespace GISProject.Models
{
    public class Trail : GeoEntity
    {
        public string Name { get; set; } = string.Empty;

        public TrailType TrailType { get; set; } = TrailType.Unknown;

        public double? EstimatedLengthMeters { get; set; }

        public DifficultyLevel Difficulty { get; set; } = DifficultyLevel.Unknown;

        public long? CreatedByUserId { get; set; }

        public User? CreatedByUser { get; set; }

        public ICollection<PointOfInterest>? PointsOfInterest { get; set; }

    }

}
