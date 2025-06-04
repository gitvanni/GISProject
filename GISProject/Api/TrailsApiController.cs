using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite;
using NetTopologySuite.Geometries;
using System.Text.Json;
using GISProject.Data;
using GISProject.Models;
using GISProject.Enumerations;
using GISProject.Services.Geo;

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

        // GET by bounding box
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
                .Select(t => new
                {
                    t.Id,
                    t.Name,
                    t.Difficulty,
                    t.TrailType,
                    Coordinates = _geometryMapper.MapCoordinates(t.Geometry)
                        .Select(c => new { c.Latitude, c.Longitude })
                });

            return Ok(trails);
        }

        // GET by ID
        [HttpGet("{id}")]
        public async Task<IActionResult> GetById(long id)
        {
            var trail = await _context.Trails.FindAsync(id);
            if (trail == null)
                return NotFound();

            var coords = _geometryMapper.MapCoordinates(trail.Geometry)
                .Select(c => new { c.Latitude, c.Longitude });

            return Ok(new
            {
                trail.Id,
                trail.Name,
                trail.Difficulty,
                trail.TrailType,
                Coordinates = coords
            });
        }

        // POST - create trail
        [HttpPost]
        public async Task<IActionResult> Create([FromBody] JsonElement data)
        {
            try
            {
                var name = data.GetProperty("name").GetString()!;
                var difficulty = Enum.Parse<DifficultyLevel>(data.GetProperty("difficulty").GetString()!);
                var trailType = Enum.Parse<TrailType>(data.GetProperty("trailType").GetString()!);
                var coordinatesJson = data.GetProperty("geometry").GetProperty("coordinates");

                var factory = NtsGeometryServices.Instance.CreateGeometryFactory(srid: 4326);
                Geometry geometry;

                if (trailType == TrailType.Line)
                {
                    var coords = coordinatesJson.EnumerateArray().Select(c =>
                        new Coordinate(c[0].GetDouble(), c[1].GetDouble())).ToArray();

                    geometry = factory.CreateLineString(coords);
                }
                else if (trailType == TrailType.MultiLine)
                {
                    var segments = new List<LineString>();
                    foreach (var line in coordinatesJson.EnumerateArray())
                    {
                        var coords = line.EnumerateArray().Select(c =>
                            new Coordinate(c[0].GetDouble(), c[1].GetDouble())).ToArray();

                        segments.Add(factory.CreateLineString(coords));
                    }

                    geometry = factory.CreateMultiLineString(segments.ToArray());
                }
                else
                {
                    return BadRequest("Tipo di trail non supportato");
                }

                var trail = new Trail
                {
                    Name = name,
                    Difficulty = difficulty,
                    TrailType = trailType,
                    Geometry = geometry,
                    CreatedAt = DateTime.UtcNow
                };

                _context.Trails.Add(trail);
                await _context.SaveChangesAsync();

                return Ok(new { trail.Id });
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Errore durante il salvataggio: {ex.Message}");
            }
        }

        // PUT - update existing trail
        [HttpPut("{id}")]
        public async Task<IActionResult> Update(long id, [FromBody] JsonElement data)
        {
            var trail = await _context.Trails.FindAsync(id);
            if (trail == null)
                return NotFound();

            try
            {
                trail.Name = data.GetProperty("name").GetString()!;
                trail.Difficulty = Enum.Parse<DifficultyLevel>(data.GetProperty("difficulty").GetString()!);
                trail.TrailType = Enum.Parse<TrailType>(data.GetProperty("trailType").GetString()!);

                var coordinatesJson = data.GetProperty("geometry").GetProperty("coordinates");
                var factory = NtsGeometryServices.Instance.CreateGeometryFactory(srid: 4326);

                if (trail.TrailType == TrailType.Line)
                {
                    var coords = coordinatesJson.EnumerateArray().Select(c =>
                        new Coordinate(c[0].GetDouble(), c[1].GetDouble())).ToArray();

                    trail.Geometry = factory.CreateLineString(coords);
                }
                else if (trail.TrailType == TrailType.MultiLine)
                {
                    var segments = new List<LineString>();
                    foreach (var line in coordinatesJson.EnumerateArray())
                    {
                        var coords = line.EnumerateArray().Select(c =>
                            new Coordinate(c[0].GetDouble(), c[1].GetDouble())).ToArray();

                        segments.Add(factory.CreateLineString(coords));
                    }

                    trail.Geometry = factory.CreateMultiLineString(segments.ToArray());
                }

                await _context.SaveChangesAsync();
                return NoContent();
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Errore durante l'aggiornamento: {ex.Message}");
            }
        }

        // DELETE - remove trail
        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(long id)
        {
            var trail = await _context.Trails.FindAsync(id);
            if (trail == null)
                return NotFound();

            _context.Trails.Remove(trail);
            await _context.SaveChangesAsync();

            return NoContent();
        }
    }
}
