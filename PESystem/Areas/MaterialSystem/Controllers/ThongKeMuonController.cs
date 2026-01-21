using Microsoft.AspNetCore.Mvc;

namespace PESystem.Areas.MaterialSystem.Controllers
{
    [Area("MaterialSystem")]
    public class ThongKeMuonController : Controller
    {
        public IActionResult Index()
        {
            return View();
        }
    }
}
