using GISProject.Data;
using GISProject.Enum;
using GISProject.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite;
using NetTopologySuite.Geometries;
using NetTopologySuite.IO;
using System.Linq;
using System.Text.Json;

namespace GISProject.Api
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


        //Get all points of a certain category
        [HttpGet("points")]
        public IActionResult Get([FromQuery] PoiCategory category)
        {
            var filteredPoints = _context.PointsOfInterest
                .Include(p => p.PoiCategories)
                .Where(p => p.Geometry != null && p.PoiCategories.Any(c => c.Category == category))
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
                
            return Ok(filteredPoints);
        }


        //Add a point
        [HttpPost("post")]
        public async Task<IActionResult> Post([FromBody] JsonElement body)
        {
            try
            {
                // Estrai latitudine e longitudine
                var coords = body.GetProperty("geometry").GetProperty("coordinates");
                double lon = coords[0].GetDouble();
                double lat = coords[1].GetDouble();

                string name = body.GetProperty("name").GetString()!;
                string category = body
                    .GetProperty("poiCategories")[0]
                    .GetProperty("category")
                    .GetString()!;

                // Crea il punto con NetTopologySuite
                var geometryFactory = NtsGeometryServices.Instance.CreateGeometryFactory(srid: 4326);
                var point = geometryFactory.CreatePoint(new Coordinate(lon, lat));

                var poi = new PointOfInterest
                {
                    Name = name,
                    Geometry = point,
                    PoiCategories = new List<PointOfInterestCategory>
            {
                new PointOfInterestCategory
                {
                    Category = System.Enum.Parse<PoiCategory>(category)
                }
            }
                };

                _context.PointsOfInterest.Add(poi);
                await _context.SaveChangesAsync();

                return Ok(new { poi.Id });
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Errore: {ex.Message}");
            }
        }
    }
}
