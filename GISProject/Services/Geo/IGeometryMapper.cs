using NetTopologySuite.Geometries;

namespace GISProject.Services.Geo
{
    public interface IGeometryMapper
    {
            IEnumerable<(double Latitude, double Longitude)> MapCoordinates(Geometry geometry);
    }
}

