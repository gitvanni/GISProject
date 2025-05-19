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
        private readonly ApplicationDbContext _context;

        public PointsOfInterestsController(ApplicationDbContext context)
        {
            _context = context;
        }

    }
}
