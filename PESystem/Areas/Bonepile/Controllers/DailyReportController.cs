using Microsoft.AspNetCore.Mvc;

namespace PESystem.Areas.Bonepile.Controllers
{
    public class DailyReportController : Controller
    {
        public IActionResult Index()
        {
            return View();
        }
    }
}
