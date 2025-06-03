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


        //Restituisce tutti i punti di una certa categoria
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
                        Longitude = pt.X,
                        Categories = p.PoiCategories.Select(c => c.Category.ToString()).ToList()
                    };
                    
                });
                
            return Ok(filteredPoints);
        }


        //Permette di aggiungere un punto
        [HttpPost("post")]
        public async Task<IActionResult> Post([FromBody] JsonElement body)
        {
            try
            {
                //estrazione proprietà dal json
                double lon = body.GetProperty("longitude").GetDouble();
                double lat = body.GetProperty("latitude").GetDouble();               
                string? description = body.TryGetProperty("description", out var desc) ? desc.GetString() : null;
                string name = body.GetProperty("name").GetString()!;
                var categoriesJson = body.GetProperty("categories").EnumerateArray();
                var categories = categoriesJson
                    .Select(c => System.Enum.Parse<PoiCategory>(c.GetString()!))
                    .ToList();
             

                // Crea il punto con NetTopologySuite
                var geometryFactory = NtsGeometryServices.Instance.CreateGeometryFactory(srid: 4326);
                var point = geometryFactory.CreatePoint(new Coordinate(lon, lat));              
                var poi = new PointOfInterest
                {
                    Name = name,
                    Geometry = point,
                    Description = description,               
                    PoiCategories = categories.Select(cat => new PointOfInterestCategory { Category = cat }).ToList()          
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

        //Eliminare un punto
        [HttpDelete("points/{id}")]
        public async Task<IActionResult> DeletePoint(long id)
        {
            var point = await _context.PointsOfInterest.FindAsync(id);

            if (point == null)
            {
                return NotFound();
            }

            _context.PointsOfInterest.Remove(point);
            await _context.SaveChangesAsync();

            return NoContent(); // 204
        }
    }
}
