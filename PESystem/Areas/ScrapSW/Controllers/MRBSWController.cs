using Microsoft.AspNetCore.Mvc;

namespace PESystem.Areas.ScrapSW.Controllers
{
    [Area("ScrapSW")]
    public class MRBSWController : Controller
    {
        public IActionResult Index()
        {
            return View();
        }
    }
}
