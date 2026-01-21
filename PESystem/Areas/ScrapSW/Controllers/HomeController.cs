using Microsoft.AspNetCore.Mvc;

namespace PESystem.Areas.ScrapSW.Controllers
{
    [Area("ScrapSW")]
    public class HomeController : Controller
    {
        public IActionResult Index()
        {
            return View();
        }
    }
}
