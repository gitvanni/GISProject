using GISProject.Data;
using GISProject.Enumerations;
using GISProject.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite;
using NetTopologySuite.Geometries;
using NetTopologySuite.IO;
using NetTopologySuite.IO.Converters;
using System;
using System.Linq;
using System.Text.Json;
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

        // GET api/points?minLon=...&minLat=...&maxLon=...&maxLat=...
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

        // GET api/points/points?category=...
        [HttpGet("points")]
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

        // POST api/points/post
        [HttpPost("post")]
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

        // PUT api/points/points/{id}
        [HttpPut("points/{id}")]
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

        // DELETE api/points/points/{id}
        [HttpDelete("points/{id}")]
        public async Task<IActionResult> DeletePoint(long id)
        {
            var pointEntity = await _context.PointsOfInterest.FindAsync(id);
            if (pointEntity == null)
                return NotFound(new { message = "Point not found" });

            _context.PointsOfInterest.Remove(pointEntity);
            await _context.SaveChangesAsync();

            return NoContent();
        }

        // POST api/points/spatialfilter
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

        // Helper for approximate distance in degrees
        private static double GeometryDistance(double lon1, double lat1, double lon2, double lat2)
        {
            var dLon = lon2 - lon1;
            var dLat = lat2 - lat1;
            return Math.Sqrt(dLon * dLon + dLat * dLat);
        }
    }
}
