using Microsoft.AspNetCore.Mvc;

namespace PESystem.Areas.SearchData.Controllers
{
    [Area("SearchData")]
    public class HomeController : Controller
    {
        public IActionResult Index()
        {
            return View();
        }
    }
}
