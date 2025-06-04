using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using System;
using GISProject.Enumerations;

namespace GISProject.Models
{
    public class Trail
    {
        public long Id { get; set; }

        public string Name { get; set; } = string.Empty;

        public TrailType TrailType { get; set; }

        public DifficultyLevel Difficulty { get; set; } = DifficultyLevel.Unknown;

        public double? EstimatedLengthMeters { get; set; }

        public Geometry Geometry { get; set; } = default!;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public string? Description { get; set; }

        public long? CreatedByUserId { get; set; }

        public User? CreatedByUser { get; set; }

        public ICollection<PointOfInterest>? PointsOfInterest { get; set; }
    }

}
