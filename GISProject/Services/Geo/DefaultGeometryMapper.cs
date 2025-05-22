using NetTopologySuite.Geometries;

namespace GISProject.Services.Geo
{
    public class DefaultGeometryMapper : BaseGeometryMapper
    {
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
