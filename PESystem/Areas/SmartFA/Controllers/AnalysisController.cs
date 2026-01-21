using Microsoft.AspNetCore.Mvc;

namespace PESystem.Areas.SmartFA.Controllers
{
    [Area("SmartFA")]
    public class AnalysisController : Controller
    {
        public IActionResult Index()
        {
            return View(); // Return full view for direct navigation
        }

        public IActionResult Repair(string serialNumber)
        {
            ViewData["SerialNumber"] = serialNumber;
            return View("repair");
        }

    }
}
