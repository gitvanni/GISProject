using NetTopologySuite.Geometries;

namespace GISProject.Services.Geo
{
    public class DefaultGeometryMapper : BaseGeometryMapper
    {
        public override object MapRawCoordinates(Geometry geometry)
        {
            return geometry switch
            {
                Point p => new[] { p.X, p.Y },

                LineString line => line.Coordinates
                    .Select(c => new[] { c.X, c.Y })
                    .ToArray(),

                Polygon polygon => new[]
                {
                    polygon.ExteriorRing.Coordinates
                        .Select(c => new[] { c.X, c.Y })
                        .ToArray()
                },

                MultiLineString multi => multi.Geometries
                    .OfType<LineString>()
                    .Select(line => line.Coordinates
                        .Select(c => new[] { c.X, c.Y })
                        .ToArray())
                    .ToArray(),

                _ => throw new NotSupportedException($"Geometry type '{geometry.GeometryType}' non supportato")
            };
        }

        protected override IEnumerable<(double Latitude, double Longitude)> MapLineString(LineString line)
        {
            return line.Coordinates.Select(c => (c.Y, c.X));
        }

        protected override IEnumerable<(double Latitude, double Longitude)> MapMultiLineString(MultiLineString multi)
        {
            foreach (var geom in multi.Geometries)
            {
                if (geom is LineString line)
                {
                    foreach (var coord in line.Coordinates)
                        yield return (coord.Y, coord.X);
                }
            }
        }
    }
}
