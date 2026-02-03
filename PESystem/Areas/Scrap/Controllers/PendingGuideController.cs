using Microsoft.AspNetCore.Mvc;

namespace PESystem.Areas.Scrap.Controllers
{
    [Area("Scrap")]
    public class PendingGuideController : Controller
    {
        public IActionResult Index()
        {
            return View();
        }
    }
}
