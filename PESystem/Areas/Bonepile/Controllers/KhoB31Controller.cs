using Microsoft.AspNetCore.Mvc;

namespace PESystem.Areas.Bonepile.Controllers
{
    [Area("Bonepile")]
    public class KhoB31Controller : Controller
    {
        public IActionResult Index()
        {
            return View();
        }
    }
}
