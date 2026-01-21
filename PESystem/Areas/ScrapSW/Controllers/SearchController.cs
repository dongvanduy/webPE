using Microsoft.AspNetCore.Mvc;

namespace PESystem.Areas.ScrapSW.Controllers
{
    [Area("ScrapSW")]
    public class SearchController : Controller
    {
        public IActionResult Index()
        {
            return View();
        }
    }
}
