using NetTopologySuite.Geometries;

namespace GISProject.Services.Geo
{
    public abstract class BaseGeometryMapper : IGeometryMapper
    {
        public IEnumerable<(double Latitude, double Longitude)> MapCoordinates(Geometry geometry)
        {
            return geometry switch
            {
                LineString line => MapLineString(line),
                MultiLineString multi => MapMultiLineString(multi),
                _ => Enumerable.Empty<(double, double)>()
            };
        }

        protected abstract IEnumerable<(double Latitude, double Longitude)> MapLineString(LineString line);
        protected abstract IEnumerable<(double Latitude, double Longitude)> MapMultiLineString(MultiLineString multi);
    }
}
