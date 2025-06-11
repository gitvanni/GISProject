using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite;
using NetTopologySuite.Geometries;
using System.Text.Json;
using GISProject.Data;
using GISProject.Models;
using GISProject.Enumerations;
using GISProject.Services.Geo;
using NetTopologySuite.IO;
using System.Linq;

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
                    Geometry = new
                    {
                        type = t.Geometry.GeometryType,
                        coordinates = _geometryMapper.MapRawCoordinates(t.Geometry)
                    }
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

                switch (trailType)
                {
                    case TrailType.Line:
                        var coordsLine = coordinatesJson.EnumerateArray()
                            .Select(c => new Coordinate(c[0].GetDouble(), c[1].GetDouble()))
                            .ToArray();
                        geometry = factory.CreateLineString(coordsLine);
                        break;

                    case TrailType.MultiLine:
                        var segments = coordinatesJson.EnumerateArray().Select(line =>
                            factory.CreateLineString(
                                line.EnumerateArray()
                                    .Select(c => new Coordinate(c[0].GetDouble(), c[1].GetDouble()))
                                    .ToArray()
                            )).ToArray();
                        geometry = factory.CreateMultiLineString(segments);
                        break;

                    case TrailType.Polygon:
                        var ringCoords = coordinatesJson[0].EnumerateArray()
                            .Select(c => new Coordinate(c[0].GetDouble(), c[1].GetDouble()))
                            .ToList();

                        if (!ringCoords.First().Equals2D(ringCoords.Last()))
                            ringCoords.Add(ringCoords.First());

                        var ring = factory.CreateLinearRing(ringCoords.ToArray());
                        geometry = factory.CreatePolygon(ring);
                        break;

                    default:
                        return BadRequest("Tipo geometria non riconosciuto.");
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

                var difficultyRaw = data.GetProperty("difficulty");
                DifficultyLevel difficulty;

                if (difficultyRaw.ValueKind == JsonValueKind.String)
                {
                    difficulty = Enum.Parse<DifficultyLevel>(difficultyRaw.GetString()!);
                }
                else if (difficultyRaw.ValueKind == JsonValueKind.Number)
                {
                    difficulty = (DifficultyLevel)difficultyRaw.GetInt32();
                }
                else
                {
                    return BadRequest("Formato della difficoltà non valido.");
                }

                var trailTypeRaw = data.GetProperty("trailType");
                TrailType trailType;

                if (trailTypeRaw.ValueKind == JsonValueKind.String)
                {
                    trailType = Enum.Parse<TrailType>(trailTypeRaw.GetString()!);
                }
                else if (trailTypeRaw.ValueKind == JsonValueKind.Number)
                {
                    trailType = (TrailType)trailTypeRaw.GetInt32();
                }
                else
                {
                    return BadRequest("Formato del tipo trail non valido.");
                }

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
                else if (trail.TrailType == TrailType.Polygon)
                {
                    var coords = coordinatesJson.EnumerateArray().Select(c =>
                        new Coordinate(c[0].GetDouble(), c[1].GetDouble())).ToList();

                    // Verifica se il poligono è chiuso, altrimenti chiudilo
                    if (!coords.First().Equals2D(coords.Last()))
                        coords.Add(coords.First());

                    var ring = factory.CreateLinearRing(coords.ToArray());
                    trail.Geometry = factory.CreatePolygon(ring);
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

        [HttpPost("spatialfilter")]
        public IActionResult SpatialFilter([FromBody] JsonElement data)
        {
            try
            {
                var geomJson = data.GetProperty("geometry").ToString();
                var reader = new GeoJsonReader();
                var filterGeometry = reader.Read<Geometry>(geomJson);
                filterGeometry.SRID = 4326;

                var result = _context.Trails
                    .Where(t => t.Geometry != null && t.Geometry.Intersects(filterGeometry))
                    .Take(1000)
                    .ToList()
                    .Select(t =>
                    {
                        var coords = _geometryMapper.MapCoordinates(t.Geometry)
                            .Select(c => new { c.Latitude, c.Longitude });

                        return new
                        {
                            t.Id,
                            t.Name,
                            t.Difficulty,
                            t.TrailType,
                            Coordinates = coords
                        };
                    });

                return Ok(result);
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Errore nel filtro spaziale trail: {ex.Message}");
            }
        }
        
        //Gets trails based on the difficulty
        [HttpGet("bydifficulty")]
        public IActionResult GetByDifficulty([FromQuery] string level,
                                     [FromQuery] double minLon, [FromQuery] double minLat,
                                     [FromQuery] double maxLon, [FromQuery] double maxLat)
        {
            if (!Enum.TryParse<DifficultyLevel>(level, ignoreCase: true, out var parsedLevel))
                return BadRequest("Difficoltà non valida");

            var envelope = new Envelope(minLon, maxLon, minLat, maxLat);
            var box = Geometry.DefaultFactory.ToGeometry(envelope);
            box.SRID = 4326;

            var trails = _context.Trails
                .Where(t => t.Geometry != null &&
                            t.Geometry.Intersects(box) &&
                            t.Difficulty == parsedLevel)
                .Take(1000)
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

        [HttpGet("filter")]
        public IActionResult Filter(
            [FromQuery] string filterField,
            [FromQuery] string filterValue,
            [FromQuery] double minLon, [FromQuery] double minLat,
            [FromQuery] double maxLon, [FromQuery] double maxLat)
        {
            var envelope = new Envelope(minLon, maxLon, minLat, maxLat);
            var box = Geometry.DefaultFactory.ToGeometry(envelope);
            box.SRID = 4326;

            var query = _context.Trails
                                .Where(t => t.Geometry != null && t.Geometry.Intersects(box));

            switch (filterField.ToLower())
            {
                case "name":
                    query = query.Where(t => t.Name.Contains(filterValue));
                    break;
                case "difficulty":
                    if (Enum.TryParse<DifficultyLevel>(filterValue, true, out var d))
                        query = query.Where(t => t.Difficulty == d);
                    else
                        return BadRequest("Valore di difficulty non valido.");
                    break;
                case "trailtype":
                    if (Enum.TryParse<TrailType>(filterValue, true, out var tt))
                        query = query.Where(t => t.TrailType == tt);
                    else
                        return BadRequest("Valore di trailType non valido.");
                    break;
                default:
                    return BadRequest("filterField non supportato.");
            }

            var result = query
                .Take(500)
                .AsEnumerable()
                .Select(t => new
                {
                    t.Id,
                    t.Name,
                    t.Difficulty,
                    t.TrailType,
                    Coordinates = _geometryMapper.MapCoordinates(t.Geometry)
                                     .Select(c => new { c.Latitude, c.Longitude })
                });

            return Ok(result);
        }

        // 2) Filtro per area (solo poligoni)
        // GET api/trailsapi/byarea?minArea=10000
        // GET api/trailsapi/byarea?minArea=…&minLon=…&minLat=…&maxLon=…&maxLat=…
        [HttpGet("byarea")]
        public IActionResult GetByArea(
            [FromQuery] double minArea,
            [FromQuery] double minLon, [FromQuery] double minLat,
            [FromQuery] double maxLon, [FromQuery] double maxLat)
        {
            var env = new Envelope(minLon, maxLon, minLat, maxLat);
            var box = Geometry.DefaultFactory.ToGeometry(env);
            box.SRID = 4326;

            var trails = _context.Trails
                .Where(t =>
                    t.Geometry != null
                    && t.TrailType == TrailType.Polygon
                    && t.Geometry.Intersects(box)
                    && t.Geometry.Area >= minArea
                )
                .Take(500)
                .AsEnumerable()
                .Select(t => new {
                    t.Id,
                    t.Name,
                    t.Difficulty,
                    t.TrailType,
                    Area = t.Geometry.Area,
                    Coordinates = _geometryMapper
                        .MapCoordinates(t.Geometry)
                        .Select(c => new { c.Latitude, c.Longitude })
                });

            return Ok(trails);
        }

        // 3) Filtro per lunghezza (solo linee e multilinee)
        // GET api/trailsapi/bylength?minLength=500
        [HttpGet("bylength")]
        public IActionResult GetByLength(
        [FromQuery] double minLength,
        [FromQuery] double minLon, [FromQuery] double minLat,
        [FromQuery] double maxLon, [FromQuery] double maxLat)
        {
            var env = new Envelope(minLon, maxLon, minLat, maxLat);
            var box = Geometry.DefaultFactory.ToGeometry(env);
            box.SRID = 4326;

            var trails = _context.Trails
                .Where(t =>
                    t.Geometry != null
                    && (t.TrailType == TrailType.Line || t.TrailType == TrailType.MultiLine)
                    && t.Geometry.Intersects(box)
                    && t.Geometry.Length >= minLength
                )
                .Take(500)
                .AsEnumerable()
                .Select(t => new {
                    t.Id,
                    t.Name,
                    t.Difficulty,
                    t.TrailType,
                    Length = t.Geometry.Length,
                    Coordinates = _geometryMapper
                        .MapCoordinates(t.Geometry)
                        .Select(c => new { c.Latitude, c.Longitude })
                });

            return Ok(trails);
        }

        // 4) Nearest-neighbor: N trail più vicini a un punto
        // GET api/trailsapi/nearest?lon=12.5&lat=42.5&n=5
        [HttpGet("nearest")]
        public IActionResult GetNearest(
            [FromQuery] double lon,
            [FromQuery] double lat,
            [FromQuery] int n = 5)
        {
            var factory = NtsGeometryServices.Instance.CreateGeometryFactory(srid: 4326);
            var pt = factory.CreatePoint(new Coordinate(lon, lat));
            pt.SRID = 4326;

            var nearest = _context.Trails
                .Where(t => t.Geometry != null)
                .Select(t => new
                {
                    Trail = t,
                    Distance = t.Geometry.Distance(pt)
                })
                .OrderBy(x => x.Distance)
                .Take(n)
                .AsEnumerable()
                .Select(x => new
                {
                    x.Trail.Id,
                    x.Trail.Name,
                    x.Trail.Difficulty,
                    x.Trail.TrailType,
                    Distance = x.Distance,
                    Coordinates = _geometryMapper.MapCoordinates(x.Trail.Geometry)
                                     .Select(c => new { c.Latitude, c.Longitude })
                });

            return Ok(nearest);
        }

    }
}
