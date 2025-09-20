using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using ALab_Cabinet.Models;
using ALab_Cabinet.Utils;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;

namespace ALab_Cabinet.Controllers;

public class TelegramAuthController(
    ILogger<TelegramAuthController> logger,
    IConfiguration configuration, 
    ManagerObject<Config> configManager
) : Controller
{
    private Config _config => configManager.Obj ?? new Config();
    private readonly IConfiguration _configuration = configuration;
    
    public IActionResult Index()
    {
        return View(new TelegramModel() { BotName = _config.BotName});
    }
    
    public async Task<IActionResult> Authenticate(
        [FromQuery]string id, 
        [FromQuery]string first_name, 
        [FromQuery]string last_name, 
        [FromQuery]string username, 
        [FromQuery]string photo_url, 
        [FromQuery]string auth_date, 
        [FromQuery]string hash
    )
    {
        // Проверка подписи от Telegram
        if (!VerifyTelegramSignature(hash, id, first_name, last_name, username, photo_url, auth_date)) return Unauthorized("Invalid signature");
        if (!_config.Admins.Contains(id)) return Unauthorized("You aren't admin");
        
        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.NameIdentifier, id),
            new Claim(ClaimTypes.Name, username)
        };

        var claimsIdentity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);

        var authProperties = new AuthenticationProperties
        {
            IsPersistent = true,
            AllowRefresh = true,
            ExpiresUtc = DateTimeOffset.UtcNow.AddDays(7),
        };

        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(claimsIdentity),
            authProperties);
        
        return RedirectToAction(nameof(AdminViewController.Index), "AdminView");
    }
    
    private bool VerifyTelegramSignature(string hash, string id, string first_name, string last_name, string username, string photo_url, string auth_date)
    {
        var data = new Dictionary<string, string>
        {
            { "id", id },
            { "first_name", first_name },
            { "last_name", last_name },
            { "username", username },
            { "photo_url", photo_url },
            { "auth_date", auth_date }
        };
        
        var dataCheckString = string.Join("\n", data
            .Where(kv => !string.IsNullOrEmpty(kv.Value)) 
            .OrderBy(kv => kv.Key)
            .Select(kv => $"{kv.Key}={kv.Value}"));
        
        var secretKey = SHA256.HashData(Encoding.UTF8.GetBytes(_config.BotToken));
        
        using var hmac = new HMACSHA256(secretKey);
        var computedHash = hmac.ComputeHash(Encoding.UTF8.GetBytes(dataCheckString));
        var computedHashHex = BitConverter.ToString(computedHash).Replace("-", "").ToLower();
        
        return computedHashHex == hash;
    }
}