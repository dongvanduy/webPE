using Microsoft.AspNetCore.Mvc;

namespace API_WEB.Controllers.Bonepile
{
    public class ReportController : Controller
    {
        public IActionResult Index()
        {
            return View();
        }
    }
}
