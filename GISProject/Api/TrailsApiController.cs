using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite;
using NetTopologySuite.Geometries;
using NetTopologySuite.IO.Converters;
using GISProject.Data;
using GISProject.Models;
using GISProject.Enumerations;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GISProject.Api
{
    [Route("api/[controller]")]
    [ApiController]
    public class TrailsApiController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly GeometryFactory _factory;

        public TrailsApiController(ApplicationDbContext context)
        {
            _context = context;
            _factory = NtsGeometryServices.Instance.CreateGeometryFactory(srid: 4326);
        }

        //Get trails based on map position
        [HttpGet]
        public IActionResult Get([FromQuery] double minLon, [FromQuery] double minLat,
                                 [FromQuery] double maxLon, [FromQuery] double maxLat)
        {
            var env = new Envelope(minLon, maxLon, minLat, maxLat);
            var box = _factory.ToGeometry(env);

            var trails = _context.Trails
                .Where(t => t.Geometry != null && t.Geometry.Intersects(box))
                .Take(500)
                .AsEnumerable()
                .ToList();

            var features = trails.Select(t => new
            {
                type = "Feature",
                geometry = t.Geometry,     
                properties = new
                {
                    id = t.Id,
                    name = t.Name,
                    difficulty = t.Difficulty,
                    trailType = t.TrailType
                }
            });

            var featureCollection = new
            {
                type = "FeatureCollection",
                features
            };

            return Ok(featureCollection);
        }

        //Create a trail
        [HttpPost]
        public async Task<IActionResult> Create([FromBody] JsonElement data)
        {
            try
            {
                // Read and parse fields
                var name = data.GetProperty("name").GetString()!;
                var diffRaw = data.GetProperty("difficulty");
                var difficulty = diffRaw.ValueKind switch
                {
                    JsonValueKind.String => Enum.Parse<DifficultyLevel>(diffRaw.GetString()!),
                    JsonValueKind.Number => (DifficultyLevel)diffRaw.GetInt32(),
                    _ => throw new Exception("Formato della difficoltà non valido.")
                };
                var typeRaw = data.GetProperty("trailType");
                var trailType = typeRaw.ValueKind switch
                {
                    JsonValueKind.String => Enum.Parse<TrailType>(typeRaw.GetString()!),
                    JsonValueKind.Number => (TrailType)typeRaw.GetInt32(),
                    _ => throw new Exception("Formato del tipo trail non valido.")
                };
                var coordsJson = data.GetProperty("geometry").GetProperty("coordinates");
                Geometry geometry;
                if (trailType == TrailType.Line)
                {
                    var coords = coordsJson.EnumerateArray()
                        .Select(c => new Coordinate(c[0].GetDouble(), c[1].GetDouble()))
                        .ToArray();
                    geometry = _factory.CreateLineString(coords);
                }
                else if (trailType == TrailType.MultiLine)
                {
                    var segments = coordsJson.EnumerateArray()
                        .Select(line => _factory.CreateLineString(
                            line.EnumerateArray()
                                .Select(c => new Coordinate(c[0].GetDouble(), c[1].GetDouble()))
                                .ToArray()))
                        .ToArray();
                    geometry = _factory.CreateMultiLineString(segments);
                }
                else if (trailType == TrailType.Polygon)
                {
                    var ring = coordsJson[0].EnumerateArray()
                        .Select(c => new Coordinate(c[0].GetDouble(), c[1].GetDouble()))
                        .ToList();
                    if (!ring.First().Equals2D(ring.Last())) ring.Add(ring.First());
                    geometry = _factory.CreatePolygon(_factory.CreateLinearRing(ring.ToArray()));
                }
                else
                {
                    return BadRequest(new { message = "Tipo geometria non riconosciuto." });
                }

                var trail = new Trail
                {
                    Name = name,
                    Difficulty = difficulty,
                    TrailType = trailType,
                    Geometry = geometry
                };

                _context.Trails.Add(trail);
                await _context.SaveChangesAsync();

                return CreatedAtAction(nameof(Get), new { id = trail.Id },
                    new
                    {
                        type = "FeatureCollection",
                        features = new[] {
                        new {
                            type = "Feature",
                            geometry = trail.Geometry,
                            properties = new {
                                id = trail.Id,
                                name = trail.Name,
                                difficulty = trail.Difficulty,
                                trailType = trail.TrailType
                            }
                        }
                    }
                    });
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        //Update a trail
        [HttpPut("{id}")]
        public async Task<IActionResult> Update(long id, [FromBody] JsonElement data)
        {
            var trail = await _context.Trails.FindAsync(id);
            if (trail == null)
                return NotFound(new { message = "Trail non trovato" });

            try
            {
                // nome, enum e geometria (come prima)
                trail.Name = data.GetProperty("name").GetString()!;

                var diffRaw = data.GetProperty("difficulty");
                trail.Difficulty = diffRaw.ValueKind switch
                {
                    JsonValueKind.String => Enum.Parse<DifficultyLevel>(diffRaw.GetString()!),
                    JsonValueKind.Number => (DifficultyLevel)diffRaw.GetInt32(),
                    _ => throw new Exception("Formato della difficoltà non valido.")
                };

                var typeRaw = data.GetProperty("trailType");
                trail.TrailType = typeRaw.ValueKind switch
                {
                    JsonValueKind.String => Enum.Parse<TrailType>(typeRaw.GetString()!),
                    JsonValueKind.Number => (TrailType)typeRaw.GetInt32(),
                    _ => throw new Exception("Formato del tipo trail non valido.")
                };

                var coordsJson = data.GetProperty("geometry").GetProperty("coordinates");
                if (trail.TrailType == TrailType.Line)
                {
                    var coords = coordsJson.EnumerateArray()
                        .Select(c => new Coordinate(c[0].GetDouble(), c[1].GetDouble()))
                        .ToArray();
                    trail.Geometry = _factory.CreateLineString(coords);
                }
                else if (trail.TrailType == TrailType.MultiLine)
                {
                    var segments = coordsJson.EnumerateArray()
                        .Select(line => _factory.CreateLineString(
                            line.EnumerateArray()
                                .Select(c => new Coordinate(c[0].GetDouble(), c[1].GetDouble()))
                                .ToArray()))
                        .ToArray();
                    trail.Geometry = _factory.CreateMultiLineString(segments);
                }
                else if (trail.TrailType == TrailType.Polygon)
                {
                    var ring = coordsJson[0].EnumerateArray()
                        .Select(c => new Coordinate(c[0].GetDouble(), c[1].GetDouble()))
                        .ToList();
                    if (!ring.First().Equals2D(ring.Last()))
                        ring.Add(ring.First());
                    trail.Geometry = _factory.CreatePolygon(_factory.CreateLinearRing(ring.ToArray()));
                }
                else
                {
                    return BadRequest(new { message = "Tipo geometria non riconosciuto." });
                }

                await _context.SaveChangesAsync();
                return NoContent();
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = $"Errore durante l'aggiornamento: {ex.Message}" });
            }
        }

        //Delete a trail
        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(long id)
        {
            var trail = await _context.Trails.FindAsync(id);
            if (trail == null) return NotFound(new { message = "Trail non trovato" });

            _context.Trails.Remove(trail);
            await _context.SaveChangesAsync();
            return NoContent();
        }


        //Get trails by difficulty
        [HttpGet("bydifficulty")]
        public IActionResult GetByDifficulty([FromQuery] string level,
                                             [FromQuery] double minLon, [FromQuery] double minLat,
                                             [FromQuery] double maxLon, [FromQuery] double maxLat)
        {
            if (!Enum.TryParse<DifficultyLevel>(level, true, out var parsedLevel))
                return BadRequest(new { message = "Difficoltà non valida" });

            var env = new Envelope(minLon, maxLon, minLat, maxLat);
            var box = _factory.ToGeometry(env);

            var trails = _context.Trails
                .Where(t => t.Geometry != null
                         && t.Geometry.Intersects(box)
                         && t.Difficulty == parsedLevel)
                .Take(1000)
                .AsEnumerable()
                .ToList();

            var features = trails.Select(t => new
            {
                type = "Feature",
                geometry = t.Geometry,
                properties = new
                {
                    id = t.Id,
                    name = t.Name,
                    difficulty = t.Difficulty,
                    trailType = t.TrailType
                }
            });

            var featureCollection = new
            {
                type = "FeatureCollection",
                features
            };

            return Ok(featureCollection);
        }

        //Spatial filter for trails in a certain area
        [HttpPost("spatialfilter")]
        public IActionResult SpatialFilter([FromBody] JsonElement data)
        {
            try
            {
                var geomJson = data.GetProperty("geometry").ToString();
                var reader = new GeoJsonConverterFactory().CreateConverter(typeof(Geometry), new JsonSerializerOptions());
                var filterGeom = new NetTopologySuite.IO.GeoJsonReader().Read<Geometry>(geomJson);

                filterGeom.SRID = 4326;
                var trails = _context.Trails
                    .Where(t => t.Geometry != null && t.Geometry.Intersects(filterGeom))
                    .Take(1000)
                    .AsEnumerable()
                    .ToList();

                var features = trails.Select(t => new
                {
                    type = "Feature",
                    geometry = t.Geometry,
                    properties = new
                    {
                        id = t.Id,
                        name = t.Name,
                        difficulty = t.Difficulty,
                        trailType = t.TrailType
                    }
                });

                var featureCollection = new
                {
                    type = "FeatureCollection",
                    features
                };

                return Ok(featureCollection);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = $"Errore nel filtro spaziale: {ex.Message}" });
            }
        }

        //Generci filter for all trails
        [HttpGet("filter")]
        public IActionResult Filter([FromQuery] string filterField,
                                    [FromQuery] string filterValue,
                                    [FromQuery] double minLon, [FromQuery] double minLat,
                                    [FromQuery] double maxLon, [FromQuery] double maxLat)
        {
            var env = new Envelope(minLon, maxLon, minLat, maxLat);
            var box = _factory.ToGeometry(env);

            IQueryable<Trail> query = _context.Trails
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
                        return BadRequest(new { message = "Valore di difficulty non valido." });
                    break;
                case "trailtype":
                    if (Enum.TryParse<TrailType>(filterValue, true, out var tt))
                        query = query.Where(t => t.TrailType == tt);
                    else
                        return BadRequest(new { message = "Valore di trailType non valido." });
                    break;
                default:
                    return BadRequest(new { message = "filterField non supportato." });
            }

            var trails = query
                .Take(500)
                .AsEnumerable()
                .ToList();

            var features = trails.Select(t => new
            {
                type = "Feature",
                geometry = t.Geometry,
                properties = new
                {
                    id = t.Id,
                    name = t.Name,
                    difficulty = t.Difficulty,
                    trailType = t.TrailType
                }
            });

            return Ok(new
            {
                type = "FeatureCollection",
                features
            });
        }

        //Filter Polygons by area width
        [HttpGet("byarea")]
        public IActionResult GetByArea([FromQuery] double minArea)
        {
            var trails = _context.Trails
                .Where(t => t.Geometry != null
                         && t.TrailType == TrailType.Polygon
                         && t.Geometry.Area >= minArea)
                .Take(500)
                .AsEnumerable()
                .ToList();

            var features = trails.Select(t => new
            {
                type = "Feature",
                geometry = t.Geometry,
                properties = new
                {
                    id = t.Id,
                    name = t.Name,
                    difficulty = t.Difficulty,
                    trailType = t.TrailType,
                    area = t.Geometry.Area
                }
            });

            return Ok(new
            {
                type = "FeatureCollection",
                features
            });
        }

        //Filter trails by lenght
        [HttpGet("bylength")]
        public IActionResult GetByLength([FromQuery] double minLength,
                                         [FromQuery] double minLon, [FromQuery] double minLat,
                                         [FromQuery] double maxLon, [FromQuery] double maxLat)
        {
            var env = new Envelope(minLon, maxLon, minLat, maxLat);
            var box = _factory.ToGeometry(env);

            var trails = _context.Trails
                .Where(t => t.Geometry != null
                         && (t.TrailType == TrailType.Line || t.TrailType == TrailType.MultiLine)
                         && t.Geometry.Intersects(box)
                         && t.Geometry.Length >= minLength)
                .Take(500)
                .AsEnumerable()
                .ToList();

            var features = trails.Select(t => new
            {
                type = "Feature",
                geometry = t.Geometry,
                properties = new
                {
                    id = t.Id,
                    name = t.Name,
                    difficulty = t.Difficulty,
                    trailType = t.TrailType,
                    length = t.Geometry.Length
                }
            });

            return Ok(new
            {
                type = "FeatureCollection",
                features
            });
        }

        //Find n nearest trails
        [HttpGet("nearest")]
        public IActionResult GetNearest([FromQuery] double lon,
                                        [FromQuery] double lat,
                                        [FromQuery] int n = 5)
        {
            var pt = _factory.CreatePoint(new Coordinate(lon, lat));

            var nearest = _context.Trails
                .Where(t => t.Geometry != null)
                .AsEnumerable()
                .Select(t => new
                {
                    Trail = t,
                    Distance = t.Geometry.Distance(pt)
                })
                .OrderBy(x => x.Distance)
                .Take(n)
                .Select(x => x.Trail)
                .ToList();

            var features = nearest.Select(t => new
            {
                type = "Feature",
                geometry = t.Geometry,
                properties = new
                {
                    id = t.Id,
                    name = t.Name,
                    difficulty = t.Difficulty,
                    trailType = t.TrailType,
                    distance = Math.Round(t.Geometry.Distance(pt), 6)
                }
            });

            return Ok(new
            {
                type = "FeatureCollection",
                features
            });
        }
    }
}
