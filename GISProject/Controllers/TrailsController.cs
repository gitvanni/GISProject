using Microsoft.AspNetCore.Mvc;

namespace GISProject.Controllers
{
    public class TrailsController : Controller
    {
        public IActionResult Index()
        {
            return View();
        }
    }
}
