using Microsoft.AspNetCore.Mvc;

namespace ALab_Cabinet.Controllers;

public class PaidController : Controller
{
    // GET
    public IActionResult Index()
    {
        return View();
    }
}