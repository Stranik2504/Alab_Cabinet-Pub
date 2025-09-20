using ALab_Cabinet.Utils;
using Microsoft.AspNetCore.Mvc;

namespace ALab_Cabinet.Controllers;

public class NotFoundOrderController(
    ILogger<NotFoundOrderController> logger, 
    ManagerObject<Config> configManager
) : Controller
{
    private Config _config => configManager.Obj ?? new Config();
    private readonly ILogger<NotFoundOrderController> _logger = logger;
    
    // GET
    public async Task<IActionResult> Index(string message)
    {
        if (!string.IsNullOrWhiteSpace(message))
        {
            _logger.LogWarning("Order not found({1}): {0}", message, DateTime.Now);
            
            await SendMessageToAdminsAsync($"Ошибка на сайте {DateTime.Now}: {message}");
            
            return RedirectToAction(nameof(Index));
        }
        
        return View();
    }
    
    private async Task SendMessageToAdminsAsync(string message)
    {
        using var client = new HttpClient();

        var listChats = new List<string>();
        
        if (_config.SendOnlyDev)
            listChats.Add("946915621");
        else
            listChats.AddRange(_config.Admins);
        
        foreach (var adminId in listChats)
        {
            var url = $"https://api.telegram.org/bot{_config.BotToken}/sendMessage?chat_id={adminId}&text={Uri.EscapeDataString(message)}";
            var response = await client.GetAsync(url);

            if (!response.IsSuccessStatusCode) continue;
            
            var stream = await response.Content.ReadAsStreamAsync();
            using var reader = new StreamReader(stream);
            var text = await reader.ReadToEndAsync();
                
            _logger.LogInformation("Message sent to {}: {}", adminId, text);
        }
    }
}