using Microsoft.AspNetCore.Mvc;

namespace ALab_Cabinet.Controllers;

public class AuthController : Controller
{
    // GET
    public IActionResult Index()
    {
        return RedirectToAction(nameof(Index), "TelegramAuth");;
    }
}