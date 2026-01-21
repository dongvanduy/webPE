using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace PESystem.Areas.BiHass.Controllers
{
    [Area("HassBi")]
    [Authorize(Policy = "HassBiAccess")]
    public class HomeController : Controller
    {
        public IActionResult Index()
        {
            return View();
        }
    }
}
