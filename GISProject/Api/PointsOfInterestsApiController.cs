using GISProject.Data;
using GISProject.Enumerations;
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

        [HttpPut("points/{id}")]
        public async Task<IActionResult> UpdatePoint(long id, [FromBody] JsonElement body)
        {
            var point = await _context.PointsOfInterest.FindAsync(id);
            if (point == null) return NotFound();

            double lon = body.GetProperty("longitude").GetDouble();
            double lat = body.GetProperty("latitude").GetDouble();

            var factory = NtsGeometryServices.Instance.CreateGeometryFactory(srid: 4326);
            point.Geometry = factory.CreatePoint(new Coordinate(lon, lat));

            await _context.SaveChangesAsync();
            return NoContent();
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

        //Filtro di ricerca spaziale
        /*[HttpPost("spatialfilter")]
        public IActionResult SpatialFilter([FromBody] JsonElement data)
        {
            try
            {
                var geomJson = data.GetProperty("geometry").ToString();
                var reader = new NetTopologySuite.IO.GeoJsonReader();
                var filterGeometry = reader.Read<Geometry>(geomJson);
                filterGeometry.SRID = 4326;

                var result = _context.PointsOfInterest
                    .Include(p => p.PoiCategories)
                    .Where(p => p.Geometry != null && p.Geometry.Intersects(filterGeometry))
                    .Take(1000)
                    .ToList()
                    .Select(p => {
                        var point = (Point)p.Geometry!;
                        return new
                        {
                            p.Id,
                            p.Name,
                            Latitude = point.Y,
                            Longitude = point.X,
                            Categories = p.PoiCategories.Select(c => c.Category.ToString()).ToList()
                        };
                    });

                return Ok(result);
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Errore durante il filtro spaziale: {ex.Message}");
            }
        }*/
        [HttpPost("spatialfilter")]
        public IActionResult SpatialFilter([FromBody] JsonElement data)
        {
            try
            {
                var geomJson = data.GetProperty("geometry").ToString();
                var reader = new NetTopologySuite.IO.GeoJsonReader();
                var filterGeometry = reader.Read<Geometry>(geomJson);
                filterGeometry.SRID = 4326;

                var groupedPoints = _context.PointsOfInterest
                    .Include(p => p.PoiCategories)
                    .Where(p => p.Geometry != null && p.Geometry.Intersects(filterGeometry))
                    .ToList()
                    .GroupBy(p => p.Geometry!.AsText()) // raggruppa per geometria come WKT
                    .Select(g =>
                    {
                        var first = g.First(); // scegli un punto come rappresentante
                        var point = (Point)first.Geometry!;
                        var allCategories = g
                            .SelectMany(p => p.PoiCategories)
                            .Select(c => c.Category.ToString())
                            .Distinct()
                            .ToList();

                        return new
                        {
                            first.Id,
                            first.Name,
                            Latitude = point.Y,
                            Longitude = point.X,
                            Categories = allCategories
                        };
                    });

                return Ok(groupedPoints);
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Errore durante il filtro spaziale: {ex.Message}");
            }
        }


        //Retrieves all the points within the radius
        /*[HttpGet("nearby")]
        public IActionResult GetNearbyPoints(double longitude, double latitude, double radiusMeters)
        {
            var geometryFactory = NtsGeometryServices.Instance.CreateGeometryFactory(srid: 4326);
            var userLocation = geometryFactory.CreatePoint(new Coordinate(longitude, latitude));

            double radiusDegrees = radiusMeters / 111320.0;



            var nearbyPoints = _context.PointsOfInterest
                 .Include(p => p.PoiCategories)
                 .Where(p => p.Geometry != null &&
                             p.Geometry.Distance(userLocation) <= radiusDegrees)
                 .ToList()
                 .Select(p =>
                 {
                     var point = (Point)p.Geometry!;
                     return new
                     {
                         p.Id,
                         p.Name,
                         Latitude = point.Y,
                         Longitude = point.X,
                         Categories = p.PoiCategories.Select(c => c.Category.ToString()).ToList()
                     };
                 });
            return Ok(nearbyPoints);
        }
        */

        [HttpGet("nearby")]
        public IActionResult GetNearbyPoints(double longitude, double latitude, double radiusMeters)
        {
            var geometryFactory = NtsGeometryServices.Instance.CreateGeometryFactory(srid: 4326);
            var userLocation = geometryFactory.CreatePoint(new Coordinate(longitude, latitude));

            // Approssimazione per convertire metri in gradi (valida vicino all'equatore)
            double radiusDegrees = radiusMeters / 111320.0;

            var groupedPoints = _context.PointsOfInterest
                .Include(p => p.PoiCategories)
                .Where(p => p.Geometry != null &&
                            p.Geometry.Distance(userLocation) <= radiusDegrees)
                .ToList()
                .GroupBy(p => p.Geometry!.AsText()) // Usa AsText per raggruppare le geometrie
                .Select(g =>
                {
                    var first = g.First(); // punto rappresentativo
                    var point = (Point)first.Geometry!;
                    var allCategories = g
                        .SelectMany(p => p.PoiCategories)
                        .Select(c => c.Category.ToString())
                        .Distinct()
                        .ToList();

                    return new
                    {
                        first.Id,
                        first.Name,
                        Latitude = point.Y,
                        Longitude = point.X,
                        Categories = allCategories
                    };
                });

            return Ok(groupedPoints);
        }


        //Retrieves nearest N points
        /* [HttpGet("nearest")]
         public IActionResult GetNearestPoints(double longitude, double latitude, int count = 5)
         {
             var geometryFactory = NtsGeometryServices.Instance.CreateGeometryFactory(srid: 4326);
             var userLocation = geometryFactory.CreatePoint(new Coordinate(longitude, latitude));

             var nearestPoints = _context.PointsOfInterest
                 .Include(p => p.PoiCategories)
                 .Where(p => p.Geometry != null)
                 .OrderBy(p => p.Geometry!.Distance(userLocation))
                 .Take(count)
                 .ToList()
                 .Select(p =>
                 {
                     var point = (Point)p.Geometry!;
                     return new
                     {
                         p.Id,
                         p.Name,
                         Latitude = point.Y,
                         Longitude = point.X,
                         Categories = p.PoiCategories.Select(c => c.Category.ToString()).ToList()
                     };
                 });

             return Ok(nearestPoints);
         }
        */
        [HttpGet("nearest")]
        public IActionResult GetNearestPoints(double longitude, double latitude, int count = 5)
        {
            var geometryFactory = NtsGeometryServices.Instance.CreateGeometryFactory(srid: 4326);
            var userLocation = geometryFactory.CreatePoint(new Coordinate(longitude, latitude));

            // Sovracampionamento: prendiamo 3x più punti del necessario
            int oversample = count * 3;

            var rawPoints = _context.PointsOfInterest
                .Include(p => p.PoiCategories)
                .Where(p => p.Geometry != null)
                .OrderBy(p => p.Geometry!.Distance(userLocation))
                .Take(oversample)
                .ToList();

            var grouped = rawPoints
                .GroupBy(p => new { X = ((Point)p.Geometry!).X, Y = ((Point)p.Geometry!).Y })
                .Select(g =>
                {
                    var first = g.First();
                    var point = (Point)first.Geometry!;
                    return new
                    {
                        Id = first.Id, // o un array se vuoi mostrare tutti gli ID
                        Name = first.Name,
                        Latitude = point.Y,
                        Longitude = point.X,
                        Categories = g.SelectMany(p => p.PoiCategories)
                                      .Select(c => c.Category.ToString())
                                      .Distinct()
                                      .ToList()
                    };
                })
                .OrderBy(p => GeometryDistance(p.Longitude, p.Latitude, longitude, latitude))
                .Take(count);

            return Ok(grouped);
        }

        // Funzione ausiliaria per calcolare distanza approssimativa tra due punti geografici
        private static double GeometryDistance(double lon1, double lat1, double lon2, double lat2)
        {
            var dLon = lon2 - lon1;
            var dLat = lat2 - lat1;
            return Math.Sqrt(dLon * dLon + dLat * dLat);
        }
    }
}