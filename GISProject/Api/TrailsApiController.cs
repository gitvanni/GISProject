using Microsoft.AspNetCore.Mvc;
using GISProject.Data;
using GISProject.Services.Geo;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;

namespace GISProject.Api
{
    [Route("api/[controller]")]
    [ApiController]
    public class TrailsApiController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly IGeometryMapper _geometryMapper;

        public TrailsApiController(ApplicationDbContext context, IGeometryMapper mapper)
        {
            _context = context;
            _geometryMapper = mapper;
        }

        [HttpGet]
        public IActionResult Get([FromQuery] double minLon, [FromQuery] double minLat,
                                 [FromQuery] double maxLon, [FromQuery] double maxLat)
        {
            var envelope = new Envelope(minLon, maxLon, minLat, maxLat);
            var box = Geometry.DefaultFactory.ToGeometry(envelope);
            box.SRID = 4326;

            var trails = _context.Trails
                .Where(t => t.Geometry != null && t.Geometry.Intersects(box))
                .Take(500)
                .ToList()
                .Select(t => new {
                    t.Id,
                    t.Name,
                    t.Difficulty,
                    Coordinates = _geometryMapper.MapCoordinates(t.Geometry)
                        .Select(c => new { c.Latitude, c.Longitude })
                });

            return Ok(trails);
        }
    }
}
