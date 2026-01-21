using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace PESystem.Controllers
{

    [Authorize]
    public class HomeController : Controller
    {
        public IActionResult Home()
        {

            return View();
        }
    }
}