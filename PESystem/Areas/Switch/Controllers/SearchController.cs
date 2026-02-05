using Microsoft.AspNetCore.Mvc;

namespace PESystem.Areas.Switch.Controllers
{
    [Area("Switch")]
    public class SearchController : Controller
    {
        public IActionResult Index()
        {
            return View();
        }
    }
}
