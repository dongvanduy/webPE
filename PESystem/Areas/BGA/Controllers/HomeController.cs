using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace PESystem.Areas.BGA.Controllers
{
    [Area("BGA")]
    [Authorize]
    public class HomeController : Controller
    {
        public IActionResult Index()
        {
            return RedirectToAction(nameof(Process));
        }

        public IActionResult Process()
        {
            return View();
        }

        public IActionResult Dashboard()
        {
            return View();
        }

        public IActionResult Actions()
        {
            return View();
        }

        public IActionResult Search()
        {
            return View();
        }
    }
}
