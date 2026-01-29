using Microsoft.AspNetCore.Mvc;

namespace PESystem.Areas.DDR.Controllers
{
    [Area("DDR")]
    public class InputController : Controller
    {
        public IActionResult Index()
        {
            return View();
        }
    }
}
