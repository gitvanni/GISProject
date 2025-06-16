using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite;
using NetTopologySuite.Geometries;
using NetTopologySuite.IO.Converters;
using GISProject.Data;
using GISProject.Models;
using GISProject.Enumerations;
using System.Text.Json;

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
                .Take(5000)
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

        //Returns all the trails
        [HttpGet("all")]
        public IActionResult GetAll([FromQuery] bool excludeUnknown = false)
        {
            var query = _context.Trails.AsQueryable();
            if (excludeUnknown)
                query = query.Where(t => t.Difficulty != DifficultyLevel.Unknown);

            var trails = query
                .Where(t => t.Geometry != null)
                .AsEnumerable()
                .ToList();

            var features = trails.Select(t => new {
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

        //Generic filter for all trails
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

        //Returns n nearest trails
        [HttpGet("nearest")]
        public IActionResult GetNearest([FromQuery] long id, [FromQuery] int count = 5)
        {
            var selected = _context.Trails.Find(id);
            if (selected == null)
                return NotFound(new { message = "Trail non trovato." });

            selected.Geometry.SRID = 4326;

            var nearestTrails = _context.Trails
                .Where(t => t.Geometry != null && t.Id != id)
                .OrderBy(t => t.Geometry.Distance(selected.Geometry))
                .Take(count)
                .AsEnumerable() 
                .ToList();

            var features = nearestTrails.Select(t => new
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

        // Filter intersection of trails
        [HttpGet("multiline-intersect")]
        public IActionResult GetMultiLineAndIntersect(
            [FromQuery] double minLon, [FromQuery] double minLat,
            [FromQuery] double maxLon, [FromQuery] double maxLat)
        {
            var env = new Envelope(minLon, maxLon, minLat, maxLat);
            var box = _factory.ToGeometry(env);

            var all = _context.Trails
                .Where(t => t.Geometry != null && t.Geometry.Intersects(box))
                .AsEnumerable()
                .ToList();

            var multilines = all.Where(t => t.TrailType == TrailType.MultiLine);

            var inters = new HashSet<Trail>();
            for (int i = 0; i < all.Count; i++)
            {
                for (int j = i + 1; j < all.Count; j++)
                {
                    var t1 = all[i];
                    var t2 = all[j];
                    if (t1.Geometry.Intersects(t2.Geometry))
                    {
                        inters.Add(t1);
                        inters.Add(t2);
                    }
                }
            }

            var result = multilines.Concat(inters).Distinct();

            var features = result.Select(t => new {
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

            return Ok(new { type = "FeatureCollection", features });
        }


        // Filter trails inside polygons
        [HttpGet("polygons-contained")]
        public IActionResult GetPolygonsAndContained(
            [FromQuery] double minLon, [FromQuery] double minLat,
            [FromQuery] double maxLon, [FromQuery] double maxLat)
        {
            var env = new Envelope(minLon, maxLon, minLat, maxLat);
            var box = _factory.ToGeometry(env);

            var all = _context.Trails
                .Where(t => t.Geometry != null && t.Geometry.Intersects(box))
                .AsEnumerable()
                .ToList();

            var polygons = all.Where(t => t.TrailType == TrailType.Polygon);

            var contained = new HashSet<Trail>();
            foreach (var poly in polygons)
            {
                foreach (var t in all.Where(x => x.TrailType != TrailType.Polygon))
                {
                    if (poly.Geometry.Contains(t.Geometry))
                    {
                        contained.Add(t);
                        contained.Add(poly);
                    }
                }
            }

            var features = contained.Select(t => new {
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

            return Ok(new { type = "FeatureCollection", features });
        }
    }
}
