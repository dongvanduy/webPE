using Microsoft.AspNetCore.Mvc;

namespace PESystem.Areas.Bonepile.Controllers
{
    [Area("Bonepile")]
    public class Bonepile2Controller : Controller
    {
        public IActionResult Index()
        {
            return View();
        }
    }
}
