using ALab_Cabinet.Models;
using ALab_Cabinet.Utils;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ALab_Cabinet.Controllers;

public class AdminViewController(
    DbManager dbManager,
    Tinkoff tinkoff,
    ManagerObject<Config> managerConfig,
    ManagerObject<List<ExpiresUserOrder>> managerObject   
) : Controller
{
    [Authorize]
    public IActionResult Index()
    {
        managerObject.Load();

        return managerObject.Obj == null ? View(null) : View(new ExpiresUserOrderModel() { Periods = managerObject.Obj });
    }
    
    [HttpPost]
    public async Task<IActionResult> Update()
    {
        await UpdaterData.NewThreadCheckAll(new CheckAllParams()
        {
            Db = dbManager.Main,
            Data = dbManager.Data,
            Generator = tinkoff,
            LoginGen = managerConfig.Obj?.TinkoffTerminalKey ?? string.Empty,
            PasswordGen = managerConfig.Obj?.TinkoffPassword ?? string.Empty,
            Config = managerConfig.Obj ?? new Config(),
            ManagerPeriods = managerObject
        });

        return Ok();
    }
}