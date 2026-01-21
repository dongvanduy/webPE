using Microsoft.AspNetCore.Mvc;

namespace PESystem.Areas.Allpart.Controllers
{
    public class FailATEController : Controller
    {
        [Area("Allpart")]
        public IActionResult Index()
        {
            return View();
        }
    }
}
