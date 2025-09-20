using System.Globalization;
using Newtonsoft.Json;
using Serilog;

namespace ALab_Cabinet.Utils;

public static class UpdaterData
{
    private static Timer? _timer;

    public static void StartDailyCheck(CheckAllParams checkAllParams)
    {
        var now = DateTime.Now;
        var firstRun = new DateTime(now.Year, now.Month, now.Day, 1, 0, 0, 0);
        
        if (now > firstRun)
            firstRun = firstRun.AddDays(1);

        var timeToGo = firstRun - now;
        
        _timer = new Timer(async x =>
        {
            await NewThreadCheckAll(checkAllParams);
        }, null, timeToGo, TimeSpan.FromDays(1));
    }
    
    public static async Task NewThreadCheckAll(CheckAllParams checkAllParams)
    {
        await Task.Run(async () => await CheckAll(checkAllParams));
    }
    
    private static async Task CheckAll(CheckAllParams checkAllParams)
    {
        var db = checkAllParams.Db;
        var data = checkAllParams.Data;
        
        var periods = new List<ExpiresUserOrder>();
        
        await foreach (var (dataDict, id) in db.GetAllRecords("Сделки"))
        {
            if (string.IsNullOrWhiteSpace(id)) continue;
            if (string.IsNullOrWhiteSpace(dataDict.GetString("Оплатить до"))) continue;
            if (string.IsNullOrWhiteSpace(dataDict.GetString("Сумма к оплате"))) continue;
            if (!dataDict.GetString("Оплатить до").TryParseDateTime(out var date)) continue;
            if (!int.TryParse(dataDict.GetString("Сумма к оплате"), out var sum)) continue;

            var periodsString = dataDict.GetString("Рассрочка");

            if (string.IsNullOrWhiteSpace(periodsString) || periodsString.Split(',').All(x => x.Split().Length < 3))
            {
                if (date >= DateTime.Now) continue;
                
                var result = await CheckNeedAdd(checkAllParams, id, dataDict.GetString("Номер сделки"), date, sum);
                
                if (result != default)
                    periods.Add(result);
                
                continue;
            }
            
            var period = periodsString.Split(",");
            
            foreach (var p in period)
            {
                var pSplit = p.Split(" ");
                
                if (pSplit.Length < 2) continue;
                
                if (!pSplit[0].TryParseDateTime(out var datePeriod)) continue;
                if (datePeriod >= DateTime.Now) continue;
                
                if (!int.TryParse(pSplit[1], out var sumPeriod)) continue;
                var paymentId = pSplit.Length > 2 ? pSplit[2] : string.Empty;

                await foreach (var (dataDictPeriod, idPeriod) in data.GetAllRecords("Links", "OrderId", dataDict.GetString("Номер сделки")))
                {
                    if (string.IsNullOrWhiteSpace(idPeriod)) continue;
                    if (!string.IsNullOrWhiteSpace(paymentId) && dataDictPeriod.GetString("PaymentId") != paymentId) continue;
                    if (string.IsNullOrWhiteSpace(paymentId) &&
                        (dataDictPeriod.GetString("Date").ParseDateTime() != datePeriod ||
                         int.Parse(dataDictPeriod.GetString("Price")) != sumPeriod)) continue;

                    var result = await CheckNeedAdd(
                        checkAllParams, 
                        id, 
                        dataDict.GetString("Номер сделки"), 
                        datePeriod, 
                        sumPeriod
                    );
                
                    if (result != default)
                        periods.Add(result);
                    
                    break;
                }
            }
        }
        
        Log.Logger.Debug(JsonConvert.SerializeObject(periods));
        
        if (checkAllParams.ManagerPeriods != default)
        {
            checkAllParams.ManagerPeriods.Obj = periods;
            checkAllParams.ManagerPeriods.Save();
        }
    }
    
    private static async Task<ExpiresUserOrder?> CheckNeedAdd(
        CheckAllParams checkAllParams, 
        string dataId, 
        string orderId,
        DateTime date,
        int sum)
    {
        var (linkDict, linkId) = await checkAllParams.Data.GetRecord("Links", "OrderId", orderId);
                
        if (string.IsNullOrWhiteSpace(linkId)) return default;
        if (string.IsNullOrWhiteSpace(linkDict.GetString("PaymentId"))) return default;

        OrderInfo? paymentInfo = default;
        try 
        { 
            paymentInfo = await checkAllParams.Generator.GetPaymentInfo(
                checkAllParams.LoginGen, 
                checkAllParams.PasswordGen, 
                linkDict.GetString("PaymentId")); 
        }
        catch (Exception ex)
        { return default; }
                
        if (string.IsNullOrWhiteSpace(paymentInfo?.OrderId)) return default;
        if (paymentInfo.Status == OrderStatus.Done) return default;
                
        string? userLink = null;

        if (checkAllParams.Db is NocoDb nocoDb)
        {
            var tableId = await nocoDb.GetIdDb("Сделки");
            
            var uri = new Uri(checkAllParams.Config.ConnectionNocoDbUrl);
            var addr = $"{uri.Scheme}://{uri.Host}";
                    
            if (!string.IsNullOrWhiteSpace(tableId))
                userLink = addr + 
                           "/dashboard/#/nc/" + 
                           checkAllParams.Config.NameDbNocoDb + 
                           "/" +
                           tableId +
                           "?rowId=" + 
                           dataId;
        }

        return new ExpiresUserOrder
        {
            OrderId = orderId,
            UserLink = userLink,
            Date = date.ToString("dd.MM.yyyy", new CultureInfo("ru-RU")),
            Amount = sum.ToString(),
            Link = linkDict.GetString("Link")
        };
    }
}