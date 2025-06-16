using Dapper;
using GISProject.Data;
using GISProject.Enumerations;
using GISProject.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite;
using NetTopologySuite.Geometries;
using NetTopologySuite.IO;
using NetTopologySuite.IO.Converters;
using Npgsql;
using System;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using System.Threading.Tasks;

namespace GISProject.Api
{
    [Route("api/[controller]")]
    [ApiController]
    public class PointsOfInterestsApiController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly GeometryFactory _factory;

        public PointsOfInterestsApiController(ApplicationDbContext context)
        {
            _context = context;
            _factory = NtsGeometryServices.Instance.CreateGeometryFactory(srid: 4326);
        }

        // GET api/pointsofinterest?minLon=...&minLat=...&maxLon=...&maxLat=...
        [HttpGet]
        public IActionResult GetInBox([FromQuery] double minLon, [FromQuery] double minLat,
                                      [FromQuery] double maxLon, [FromQuery] double maxLat)
        {
            var env = new Envelope(minLon, maxLon, minLat, maxLat);
            var box = _factory.ToGeometry(env);
            box.SRID = 4326;

            var pois = _context.PointsOfInterest
                .Include(p => p.PoiCategories)
                .Where(p => p.Geometry != null && p.Geometry.Intersects(box))
                .Take(1000)
                .AsEnumerable();

            var features = pois.Select(p => new
            {
                type = "Feature",
                geometry = p.Geometry,
                properties = new
                {
                    id = p.Id,
                    name = p.Name,
                    description = p.Description,
                    categories = p.PoiCategories.Select(c => c.Category.ToString())
                }
            });

            var featureCollection = new
            {
                type = "FeatureCollection",
                features
            };

            return Ok(featureCollection);
        }

        // GET api/pointsofinterest/bycategory?category=...
        [HttpGet("bycategory")]
        public IActionResult GetByCategory([FromQuery] PoiCategory category)
        {
            var pois = _context.PointsOfInterest
                .Include(p => p.PoiCategories)
                .Where(p => p.Geometry != null && p.PoiCategories.Any(c => c.Category == category))
                .AsEnumerable();

            var features = pois.Select(p => new
            {
                type = "Feature",
                geometry = p.Geometry,
                properties = new
                {
                    id = p.Id,
                    name = p.Name,
                    description = p.Description,
                    categories = p.PoiCategories.Select(c => c.Category.ToString())
                }
            });

            return Ok(new
            {
                type = "FeatureCollection",
                features
            });
        }

        // POST api/pointsofinterestapi
        [HttpPost]
        public async Task<IActionResult> Post([FromBody] JsonElement body)
        {
            try
            {
                double lon = body.GetProperty("longitude").GetDouble();
                double lat = body.GetProperty("latitude").GetDouble();
                string name = body.GetProperty("name").GetString()!;
                string? description = body.TryGetProperty("description", out var desc)
                                      ? desc.GetString() : null;
                var categories = body.GetProperty("categories")
                                     .EnumerateArray()
                                     .Select(c => Enum.Parse<PoiCategory>(c.GetString()!))
                                     .ToList();

                var point = _factory.CreatePoint(new Coordinate(lon, lat));
                var poi = new PointOfInterest
                {
                    Name = name,
                    Geometry = point,
                    Description = description,
                    PoiCategories = categories
                        .Select(cat => new PointOfInterestCategory { Category = cat })
                        .ToList()
                };

                _context.PointsOfInterest.Add(poi);
                await _context.SaveChangesAsync();

                var feature = new
                {
                    type = "Feature",
                    geometry = poi.Geometry,
                    properties = new
                    {
                        id = poi.Id,
                        name = poi.Name,
                        description = poi.Description,
                        categories = poi.PoiCategories.Select(c => c.Category.ToString())
                    }
                };

                return CreatedAtAction(nameof(GetInBox), new { id = poi.Id },
                    new { type = "FeatureCollection", features = new[] { feature } });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = ex.Message });
            }
        }

        // PUT api/pointsofinterestapi/{id}
        [HttpPut("{id}")]
        public async Task<IActionResult> UpdatePoint(long id, [FromBody] JsonElement body)
        {
            var pointEntity = await _context.PointsOfInterest.FindAsync(id);
            if (pointEntity == null)
                return NotFound(new { message = "Point not found" });

            double lon = body.GetProperty("longitude").GetDouble();
            double lat = body.GetProperty("latitude").GetDouble();

            pointEntity.Geometry = _factory.CreatePoint(new Coordinate(lon, lat));
            await _context.SaveChangesAsync();

            return NoContent();
        }

        // DELETE api/pointsofinterestapi/{id}
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeletePoint(long id)
        {
            var pointEntity = await _context.PointsOfInterest.FindAsync(id);
            if (pointEntity == null)
                return NotFound(new { message = "Point not found" });

            _context.PointsOfInterest.Remove(pointEntity);
            await _context.SaveChangesAsync();

            return NoContent();
        }

        // POST api/pointsofinterestapi/spatialfilter
        [HttpPost("spatialfilter")]
        public IActionResult SpatialFilter([FromBody] JsonElement data)
        {
            try
            {
                var geomJson = data.GetProperty("geometry").ToString();
                var reader = new GeoJsonReader();
                var filterGeom = reader.Read<Geometry>(geomJson);
                filterGeom.SRID = 4326;

                var pois = _context.PointsOfInterest
                    .Include(p => p.PoiCategories)
                    .Where(p => p.Geometry != null && p.Geometry.Intersects(filterGeom))
                    .ToList()
                    .GroupBy(p => p.Geometry!.AsText())
                    .Select(g => g.First());

                var features = pois.Select(p => new
                {
                    type = "Feature",
                    geometry = p.Geometry,
                    properties = new
                    {
                        id = p.Id,
                        name = p.Name,
                        description = p.Description,
                        categories = p.PoiCategories.Select(c => c.Category.ToString())
                    }
                });

                return Ok(new { type = "FeatureCollection", features });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = ex.Message });
            }
        }

        // GET api/points/nearby?longitude=&latitude=&radiusMeters=
        [HttpGet("nearby")]
        public IActionResult GetNearby([FromQuery] double longitude, [FromQuery] double latitude,
                                       [FromQuery] double radiusMeters)
        {
            var userLoc = _factory.CreatePoint(new Coordinate(longitude, latitude));
            double radiusDeg = radiusMeters / 111320.0;

            var pois = _context.PointsOfInterest
                .Include(p => p.PoiCategories)
                .Where(p => p.Geometry != null && p.Geometry.Distance(userLoc) <= radiusDeg)
                .ToList()
                .GroupBy(p => p.Geometry!.AsText())
                .Select(g => g.First());

            var features = pois.Select(p => new
            {
                type = "Feature",
                geometry = p.Geometry,
                properties = new
                {
                    id = p.Id,
                    name = p.Name,
                    description = p.Description,
                    categories = p.PoiCategories.Select(c => c.Category.ToString())
                }
            });

            return Ok(new { type = "FeatureCollection", features });
        }

        // GET api/points/nearest?longitude=&latitude=&count=
        [HttpGet("nearest")]
        public IActionResult GetNearest([FromQuery] double longitude, [FromQuery] double latitude,
                                        [FromQuery] int count = 5)
        {
            var userLoc = _factory.CreatePoint(new Coordinate(longitude, latitude));
            int oversample = count * 3;

            var raw = _context.PointsOfInterest
                .Include(p => p.PoiCategories)
                .Where(p => p.Geometry != null)
                .OrderBy(p => p.Geometry!.Distance(userLoc))
                .Take(oversample)
                .ToList();

            var pois = raw
                .GroupBy(p => new { X = ((Point)p.Geometry!).X, Y = ((Point)p.Geometry!).Y })
                .Select(g => g.First())
                .OrderBy(p => GeometryDistance(
                    ((Point)p.Geometry!).X, ((Point)p.Geometry!).Y,
                    longitude, latitude))
                .Take(count);

            var features = pois.Select(p => new
            {
                type = "Feature",
                geometry = p.Geometry,
                properties = new
                {
                    id = p.Id,
                    name = p.Name,
                    description = p.Description,
                    categories = p.PoiCategories.Select(c => c.Category.ToString())
                }
            });

            return Ok(new { type = "FeatureCollection", features });
        }

        [HttpGet("dijkstra")]
        public async Task<IActionResult> GetDijkstraPath([FromQuery] double longitude, [FromQuery] double latitude, [FromQuery] long pointId)
        {
            try
            {
                // Trova il punto di partenza
                var startPoint = _factory.CreatePoint(new Coordinate(longitude, latitude));

                // Trova il POI di destinazione
                var destinationPoi = await _context.PointsOfInterest
                    .Include(p => p.PoiCategories)
                    .Where(p => p.Id == pointId)
                    .FirstOrDefaultAsync();

                if (destinationPoi == null)
                    return NotFound(new { message = "POI di destinazione non trovato." });

                // Trova il punto di destinazione
                var destinationPoint = destinationPoi.Geometry as Point;
                if (destinationPoint == null)
                    return NotFound(new { message = "POI di destinazione non ha una geometria valida." });

                // Calcola il percorso più breve (algoritmo di Dijkstra)
                var path = CalculateDijkstraPath(startPoint, destinationPoint);

                // Se non esiste un percorso valido
                if (path == null || path.Count == 0)
                    return NotFound(new { message = "Percorso non trovato." });

                return Ok(path);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = ex.Message });
            }
        }

        // Funzione che calcola il percorso usando l'algoritmo di Dijkstra
        private List<DijkstraResult> CalculateDijkstraPath(Point startPoint, Point destinationPoint)
        {
            // Trova i vertici più vicini
            var startVertexId = GetClosestVertex(startPoint);
            var endVertexId = GetClosestVertex(destinationPoint);

            // Query per calcolare il percorso con Dijkstra
            var query = @"
            SELECT * 
            FROM pgr_dijkstra(
                'SELECT id, source, target, cost, geom FROM road_network',
                @startVertexId, @endVertexId, false
            )";

            var results = _context.Database.GetDbConnection()
                 .Query<DijkstraResult>(query, new { startVertexId, endVertexId })
                 .ToList();

            return results;
        }

        // Funzione che converte il percorso in un GeoJSON
        private object ConvertPathToGeoJson(List<Coordinate> path)
        {
            var features = path.Select(c => new
            {
                type = "Feature",
                geometry = new
                {
                    type = "Point",
                    coordinates = new double[] { c.X, c.Y }
                },
                properties = new { }
            });

            return new
            {
                type = "FeatureCollection",
                features
            };
        }

        // Helper for approximate distance in degrees
        private static double GeometryDistance(double lon1, double lat1, double lon2, double lat2)
        {
            var dLon = lon2 - lon1;
            var dLat = lat2 - lat1;
            return Math.Sqrt(dLon * dLon + dLat * dLat);
        }

        private long GetClosestVertex(Point point)
        {
            var query = @"
            SELECT id
            FROM road_network_vertices_pgr
            ORDER BY the_geom <-> ST_Transform(ST_SetSRID(ST_MakePoint(@longitude, @latitude), 4326), 3857)
            LIMIT 1";

            using (var command = _context.Database.GetDbConnection().CreateCommand())
            {
                command.CommandText = query;
                command.Parameters.Add(new NpgsqlParameter("longitude", point.Coordinate.X));
                command.Parameters.Add(new NpgsqlParameter("latitude", point.Coordinate.Y));

                _context.Database.OpenConnection();
                var result = command.ExecuteScalar();
                _context.Database.CloseConnection();

                return (long)result;
            }
        }

    }
}
