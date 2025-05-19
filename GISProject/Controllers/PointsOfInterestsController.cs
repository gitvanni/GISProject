using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using NetTopologySuite.IO;
using GISProject.Data; 
using GISProject.Models;
using System;

namespace GISProject.Controllers
{
    public class PointsOfInterestsController : Controller
    {
        private readonly ApplicationDbContext _db;

        public PointsOfInterestsController(ApplicationDbContext db)
        {
            _db = db;
        }

        public async Task<IActionResult> Index()
        {
            var pois = _db.PointsOfInterest
                .AsEnumerable()
                .Where(p => p.Geometry is Point)
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
                })
                .ToList();

            return View(pois);
        }
    }
}
