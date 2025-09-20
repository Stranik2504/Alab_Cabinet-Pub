using System.Globalization;
using ALab_Cabinet.Utils;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Serilog;

CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("ru-RU");

var builder = WebApplication.CreateBuilder(args);

Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .WriteTo.File("logs/log-.txt", rollingInterval: RollingInterval.Day)
    .CreateLogger();

builder.Logging.ClearProviders().AddConsole().AddSerilog();

// Add services to the container.
builder.Services.AddControllersWithViews();
builder.Services.AddSingleton<ManagerObject<Config>>(_ =>
{
    var manager = new ManagerObject<Config>("files/config.json");
    manager.Load();
    
    return manager;
});
builder.Services.AddSingleton<ManagerObject<List<ExpiresUserOrder>>>(_ =>
{
    var manager = new ManagerObject<List<ExpiresUserOrder>>("files/cache_periods.json");
    
    return manager;
});
builder.Services.AddSingleton<DbManager>(x =>
{
    var config = x.GetRequiredService<ManagerObject<Config>>();
    
    if (config.Obj == null)
        throw new Exception("Config is null");
    
    var main = new NocoDb(
        config.Obj.ConnectionNocoDbUrl, 
        config.Obj.TokenNocoDb,
        config.Obj.NameDbNocoDb,
        x.GetRequiredService<ILogger<NocoDb>>()
    );
    main.Start();
    
    var data = new NocoDb(
        config.Obj.ConnectionNocoDbUrl, 
        config.Obj.TokenNocoDb,
        config.Obj.NameDataNocoDb,
        x.GetRequiredService<ILogger<NocoDb>>(),
        false
    );
    data.Start();
    
    return new DbManager() { Main = main, Data = data };
});

builder.Services.AddSingleton<Tinkoff>(x =>
{
    var tinkoff = new Tinkoff("files/tinkoff.json", x.GetRequiredService<ILogger<Tinkoff>>());

    return tinkoff;
});

// Добавление и настройка сессии
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromDays(30); // Время жизни сессии - 30 дней
    options.Cookie.IsEssential = true;
});

builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/TelegramAuth/Index";
    });

var app = builder.Build();

UpdaterData.StartDailyCheck(new CheckAllParams()
{
    Db = app.Services.GetRequiredService<DbManager>().Main,
    Data = app.Services.GetRequiredService<DbManager>().Data,
    Generator = app.Services.GetRequiredService<Tinkoff>(),
    LoginGen = app.Services.GetRequiredService<ManagerObject<Config>>().Obj?.TinkoffTerminalKey ?? string.Empty,
    PasswordGen = app.Services.GetRequiredService<ManagerObject<Config>>().Obj?.TinkoffPassword ?? string.Empty,
    Config = app.Services.GetRequiredService<ManagerObject<Config>>().Obj ?? new Config(),
    ManagerPeriods = app.Services.GetRequiredService<ManagerObject<List<ExpiresUserOrder>>>()
});

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.UseSession();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();
