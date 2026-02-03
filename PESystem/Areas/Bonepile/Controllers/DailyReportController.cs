using Microsoft.AspNetCore.Mvc;

namespace PESystem.Areas.Bonepile.Controllers
{
    [Area("Bonepile")]
    public class DailyReportController : Controller
    {
        public IActionResult Index()
        {
            return View();
        }
    }
}
