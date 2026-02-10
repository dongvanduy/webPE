using Microsoft.AspNetCore.Mvc;

namespace PESystem.Areas.SFC.Controllers
{
    [Area("SFC")]
    public class YieldRateController : Controller
    {
        public IActionResult Index()
        {
            return View();
        }
    }
}
