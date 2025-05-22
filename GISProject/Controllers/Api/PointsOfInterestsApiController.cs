using GISProject.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NetTopologySuite.Geometries;

namespace GISProject.Controllers.Api
{
    [Route("api/[controller]")]
    [ApiController]
    public class PointsOfInterestsApiController : ControllerBase
    {
        private readonly ApplicationDbContext _context;

        public PointsOfInterestsApiController(ApplicationDbContext context)
        {
            _context = context;
        }

        [HttpGet]
        public IActionResult Get([FromQuery] double minLon, [FromQuery] double minLat,
                                 [FromQuery] double maxLon, [FromQuery] double maxLat)
        {
            var envelope = new Envelope(minLon, maxLon, minLat, maxLat);
            var box = Geometry.DefaultFactory.ToGeometry(envelope);
            box.SRID = 4326;

            var points = _context.PointsOfInterest
                .Where(p => p.Geometry != null && p.Geometry.Intersects(box))
                .Take(1000)
                .ToList()
                .Select(p =>
                {
                    var pt = (Point)p.Geometry!;
                    return new
                    {
                        p.Id,
                        p.Name,
                        Latitude = pt.Y,
                        Longitude = pt.X
                    };
                });

            return Ok(points);
        }
    }
}
