using Microsoft.AspNetCore.Mvc;

namespace PESystem.Areas.Switch.Controllers
{
    [Area("Switch")]
    public class HomeController : Controller
    {
        public IActionResult Index()
        {
            return View();
        }
    }
}
