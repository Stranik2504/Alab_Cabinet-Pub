using System.Globalization;
using System.Net;
using System.Text.Encodings.Web;
using System.Text.RegularExpressions;
using ALab_Cabinet.Models;
using ALab_Cabinet.Utils;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace ALab_Cabinet.Controllers;

public partial class CabinetController(
    ILogger<CabinetController> logger,
    ManagerObject<Config> configManager,
    DbManager dbManager,
    Tinkoff tinkoff
) : Controller
{
    private readonly ILogger<CabinetController> _logger = logger;
    private readonly ManagerObject<Config> _configManager = configManager;
    private Config _config => _configManager.Obj ?? new Config();
    private readonly NocoDb _main = dbManager.Main;
    private readonly NocoDb _data = dbManager.Data;
    private readonly Tinkoff _tinkoff = tinkoff;
    private readonly CultureInfo _cultureInfo = new CultureInfo("RU-ru");

    public async Task<IActionResult> Index([FromQuery]string orderNumber)
    {
        var record = await _main.GetRecord("Сделки", "Номер сделки", orderNumber);

        if (string.IsNullOrWhiteSpace(record.Id) ||
            string.IsNullOrWhiteSpace(record.Fields.GetString("Сумма к оплате"))
        )
            return RedirectToAction(nameof(Index), "NotFoundOrder", routeValues: new { message = $"Ошибка (или Сумма к оплате не указана)(Номер сделки: {orderNumber})" });
        
        if (string.IsNullOrWhiteSpace(record.Fields.GetString("Оплатить до")))
            return RedirectToAction(nameof(Index), "NotFoundOrder", routeValues: new { message = $"Дата оплаты не указана(Номер сделки: {orderNumber})" });

        if (string.IsNullOrWhiteSpace(record.Fields.GetString("Рассрочка")))
            return await OnePayment(orderNumber, record);
        
        return await AnyPayments(orderNumber, record);
    }

    [HttpPost]
    public async Task<IActionResult> Pay(string orderId)
    {
        var record = await _main.GetRecord("Сделки", "Номер сделки", orderId);

        if (string.IsNullOrWhiteSpace(record.Id) ||
            string.IsNullOrWhiteSpace(record.Fields.GetString("Сумма к оплате"))
           )
            return RedirectToAction(nameof(Index), "NotFoundOrder", routeValues: new { message = $"Ошибка (или Сумма к оплате не указана)(Номер сделки: {orderId})" });
        
        if (string.IsNullOrWhiteSpace(record.Fields.GetString("Рассрочка")))
            return await OnePaymentGen(orderId, record);
        
        return await AnyPaymentsGen(orderId, record);
    }
    
    public IActionResult WaitPayment()
    {
        return View();
    }

    private async Task<IActionResult> OnePayment(string orderNumber, (Dictionary<string,object> Fields, string Id) record)
    {
        var price = int.Parse(record.Fields.GetString("Сумма к оплате"));
        var date = TryFormat(record.Fields.GetString("Оплатить до"));
        
        var linkData = await _data.GetRecord("Links", "OrderId", orderNumber);

        if (string.IsNullOrWhiteSpace(linkData.Id) || string.IsNullOrWhiteSpace(linkData.Fields.GetString("Link")))
            return View(new PaymentsModel() { OrderId = orderNumber, Sum = price, Payments = [new PaymentModel() { Price = price, Date = date }] });
        
        if (linkData.Fields.ContainsKey("PaymentId") && linkData.Fields.GetString("PaymentId").Contains('-'))
            return RedirectToAction(nameof(NotFoundOrderController.Index), "NotFoundOrder", routeValues: new { message = $"Ссылка(-и) была(-и) сгенерирована(-ы) через Сбербанк(Номер сделки: {orderNumber})" });
        
        // For testing TODO: remove this
        if (!string.IsNullOrWhiteSpace(linkData.Fields.GetString("PaymentId")) && linkData.Fields["PaymentId"].ToString() == "Test")
            return RedirectToAction(nameof(PaidController.Index), "Paid");
        
        if (price < 0)
            return RedirectToAction(nameof(PaidController.Index), "NotFoundOrder", routeValues: new { message = $"Сумма к оплате не может быть отрицательной(Номер сделки: {orderNumber})" });

        var result = await GetPaymentModel(new OrderParams() {
            Date = date, 
            Price = price, 
            PaymentId = linkData.Fields.GetString("PaymentId"),
            Link = linkData.Fields.GetString("Link"), 
            DataId = linkData.Id,
            OrderId = orderNumber,
            Record = record
        });
        
        if (result == default)
            return RedirectToAction(nameof(PaidController.Index), "NotFoundOrder", routeValues: new { message = $"Дата оплаты не указана(Номер сделки: {orderNumber})" });

        if (result.IsPaid)
            return RedirectToAction(nameof(PaidController.Index), "Paid");
        
        return View(new PaymentsModel() { OrderId = orderNumber, Sum = price, Payments = [result] });
    }
    
    private async Task<IActionResult> AnyPayments(string orderNumber, (Dictionary<string,object> Fields, string Id) record)
    {
        var allPrice = int.Parse(record.Fields.GetString("Сумма к оплате"));
        var paymentPlan = record.Fields.GetString("Рассрочка");
        var paymentPlanDict = new Dictionary<string, (int price, string paymentId)>();

        if (!string.IsNullOrWhiteSpace(paymentPlan))
        {
            var arr = paymentPlan.Split(',');
            var sum = 0;
        
            arr.ForEach(x =>
            {
                var paymentId = string.Empty;
                
                if (x.Split().Length > 2)
                    paymentId = x.Split()[2];
                
                paymentPlanDict.Add(ConvertPeriodToViewDate(x.Split()[0]), (int.Parse(x.Split()[1]), paymentId));
                sum += int.Parse(x.Split()[1]);
            });
            
            if (sum != allPrice)
                return RedirectToAction(nameof(NotFoundOrderController.Index), "NotFoundOrder", routeValues: new { message = $"Сумма рассрочки не совпадает с суммой заказа(Номер сделки: {orderNumber})" });
        }
        
        var list = new List<PaymentModel>();

        await foreach (var link in _data.GetAllRecords("Links", "OrderId", orderNumber))
        {
            if (string.IsNullOrWhiteSpace(link.Id) || 
                string.IsNullOrWhiteSpace(link.Fields.GetString("Link")) ||
                string.IsNullOrWhiteSpace(link.Fields.GetString("Price")))
                continue;
            
            if (link.Fields.ContainsKey("PaymentId") && link.Fields.GetString("PaymentId").Contains('-'))
                return RedirectToAction(nameof(NotFoundOrderController.Index), "NotFoundOrder", routeValues: new { message = $"Ссылка(-и) была(-и) сгенерирована(-ы) через Сбербанк(Номер сделки: {orderNumber})" });
            
            var result = await GetPaymentModel(new OrderParams() {
                Date = TryFormat(link.Fields.GetString("Date")), 
                Price = int.Parse(link.Fields.GetString("Price")), 
                PaymentId = link.Fields.GetString("PaymentId"),
                Link = link.Fields.GetString("Link"), 
                DataId = link.Id,
                OrderId = WebUtility.UrlEncode(orderNumber),
                Record = record
            });
                
            if (result == default)
                return RedirectToAction(nameof(PaidController.Index), "NotFoundOrder", routeValues: new { message = $"Дата оплаты не указана(Номер сделки: {orderNumber})" });

            list.Add(result);
            var cnt = paymentPlanDict.RemoveAll(x =>
            {
                var need = x.Key.ParseDateTime() == link.Fields.GetString("Date").ParseDateTime() && x.Value.price == int.Parse(link.Fields.GetString("Price"));
                
                if (!string.IsNullOrWhiteSpace(x.Value.paymentId) && !string.IsNullOrWhiteSpace(link.Fields.GetString("PaymentId")))
                    need = need && (x.Value.paymentId == link.Fields.GetString("PaymentId") || link.Fields.GetString("PaymentId") == "Test"); // TODO remove this (part 2)

                return need;
            });
            
            if (cnt != 1 && cnt != 0)
                return RedirectToAction(nameof(NotFoundOrderController.Index), "NotFoundOrder", routeValues: new { message = $"Количество периодов не совпадает с количеством ссылок на этот период(Номер сделки: {orderNumber})" });
        }
        
        foreach (var (date, (price, _)) in paymentPlanDict)
        {
            if (price < 0)
                return RedirectToAction(nameof(PaidController.Index), "NotFoundOrder", routeValues: new { message = $"Сумма к оплате не может быть отрицательной(Номер сделки: {orderNumber})" });
            
            list.Add(new PaymentModel()
            {
                Date = date,
                Price = price,
                IsPaid = false
            });
        }
        
        if (list.All(x => x.IsPaid))
            return RedirectToAction(nameof(PaidController.Index), "Paid");
        
        return View(new PaymentsModel() { OrderId = orderNumber, Sum = allPrice, Payments = list });
    }
    
    private async Task<IActionResult> OnePaymentGen(string orderNumber, (Dictionary<string,object> Fields, string Id) record)
    {
        var price = int.Parse(record.Fields.GetString("Сумма к оплате"));
        var date = TryFormat(record.Fields.GetString("Оплатить до"));
        
        var linkData = await _data.GetRecord("Links", "OrderId", orderNumber);

        if (string.IsNullOrWhiteSpace(linkData.Id) || string.IsNullOrWhiteSpace(linkData.Fields.GetString("Link")))
        {
            var orderParams = new OrderParams()
            {
                Date = date,
                Price = price,
                PaymentId = linkData.Fields.GetString("PaymentId"),
                Link = linkData.Fields.GetString("Link"),
                DataId = linkData.Id,
                OrderId = orderNumber,
                Record = record
            };
            
            orderParams.Date = await NewDate(orderParams);
            
            var res = await CreateLink(orderParams, date, 1, 1);
            
            if (!res.success)
                return RedirectToAction(nameof(PaidController.Index), "NotFoundOrder", routeValues: new { message = res.result + $"(Номер сделки: {orderNumber})" });
            
            return Redirect(res.result);
        }
        
        // For testing TODO: remove this
        if (!string.IsNullOrWhiteSpace(linkData.Fields.GetString("PaymentId")) && linkData.Fields["PaymentId"].ToString() == "Test")
            return RedirectToAction(nameof(PaidController.Index), "Paid");
        
        if (price < 0)
            return RedirectToAction(nameof(PaidController.Index), "NotFoundOrder", routeValues: new { message = $"Сумма к оплате не может быть отрицательной(Номер сделки: {orderNumber})" });

        var result = await GetOrCreateLink(new OrderParams() {
            Date = date, 
            Price = price, 
            PaymentId = linkData.Fields.GetString("PaymentId"),
            Link = linkData.Fields.GetString("Link"), 
            DataId = linkData.Id,
            OrderId = orderNumber,
            Record = record
        }, 1, 1);
        
        return result ?? RedirectToAction(nameof(PaidController.Index), "NotFoundOrder", routeValues: new { message = $"Дата оплаты не указана(Номер сделки: {orderNumber})" });
    }
    
    private async Task<IActionResult> AnyPaymentsGen(string orderNumber, (Dictionary<string,object> Fields, string Id) record)
    {
        var allPrice = int.Parse(record.Fields.GetString("Сумма к оплате"));
        var paymentPlan = record.Fields.GetString("Рассрочка");
        var paymentPlanDict = new Dictionary<string, (int price, string paymentId)>();

        if (!string.IsNullOrWhiteSpace(paymentPlan))
        {
            var arr = paymentPlan.Split(',');
            var sum = 0;
        
            arr.ForEach(x =>
            {
                var paymentId = string.Empty;
                
                if (x.Split().Length > 2)
                    paymentId = x.Split()[2];
                
                paymentPlanDict.Add(ConvertPeriodToViewDate(x.Split()[0]), (int.Parse(x.Split()[1]), paymentId));
                sum += int.Parse(x.Split()[1]);
            });
            
            if (sum != allPrice)
                return RedirectToAction(nameof(PaidController.Index), "NotFoundOrder", routeValues: new { message = $"Сумма рассрочки не совпадает с суммой заказа(Номер сделки: {orderNumber})" });
        }
        
        var list = new List<(OrderParams orderParams, bool isPaid)>();

        await foreach (var link in _data.GetAllRecords("Links", "OrderId", orderNumber))
        {
            if (string.IsNullOrWhiteSpace(link.Id) || 
                string.IsNullOrWhiteSpace(link.Fields.GetString("Link")) ||
                string.IsNullOrWhiteSpace(link.Fields.GetString("Price")))
                continue;
            
            var result = await GetPaymentModel(new OrderParams() {
                Date = TryFormat(link.Fields.GetString("Date")), 
                Price = int.Parse(link.Fields.GetString("Price")), 
                PaymentId = link.Fields.GetString("PaymentId"),
                Link = link.Fields.GetString("Link"), 
                DataId = link.Id,
                OrderId = orderNumber,
                Record = record
            });
                
            if (result == default)
                return RedirectToAction(nameof(PaidController.Index), "NotFoundOrder", routeValues: new { message = $"Дата оплаты не указана(Номер сделки: {orderNumber})" });

            list.Add((new OrderParams()
            {
                Date = result.Date, 
                Price = result.Price, 
                PaymentId = link.Fields.GetString("PaymentId"),
                Link = link.Fields.GetString("Link"), 
                DataId = link.Id,
                OrderId = orderNumber,
                Record = record
            }, result.IsPaid));
            
            var cnt = paymentPlanDict.RemoveAll(x =>
            {
                var need = x.Key.ParseDateTime() == link.Fields.GetString("Date").ParseDateTime() && x.Value.price == int.Parse(link.Fields.GetString("Price"));
                
                if (!string.IsNullOrWhiteSpace(x.Value.paymentId) && !string.IsNullOrWhiteSpace(link.Fields.GetString("PaymentId")))
                    need = need && (x.Value.paymentId == link.Fields.GetString("PaymentId") || link.Fields.GetString("PaymentId") == "Test"); // TODO remove this (part 2)

                return need;
            });
            
            if (cnt != 1 && cnt != 0)
                return RedirectToAction(nameof(PaidController.Index), "NotFoundOrder", routeValues: new { message = $"Количество периодов не совпадает с количеством ссылок на этот период(Номер сделки: {orderNumber})" });
        }
        
        foreach (var (date, (price, paymentId)) in paymentPlanDict)
        {
            list.Add((new OrderParams()
            {
                Date = date, 
                Price = price, 
                PaymentId = paymentId,
                Link = string.Empty, 
                DataId = string.Empty,
                OrderId = orderNumber,
                Record = record
            }, false));
        }

        for (var i = 0; i < list.Count; i++)
        {
            if (list[i].isPaid)
                continue;

            if (list[i].orderParams.Price < 0)
                return RedirectToAction(nameof(PaidController.Index), "NotFoundOrder", routeValues: new { message = $"Сумма к оплате не может быть отрицательной(Номер сделки: {orderNumber})" });
            
            var result = await GetOrCreateLink(list[i].orderParams, i + 1, list.Count);
            
            return result ?? RedirectToAction(nameof(PaidController.Index), "NotFoundOrder", routeValues: new { message = $"Дата оплаты не указана(Номер сделки: {orderNumber})" });
        }
        
        return RedirectToAction(nameof(Index), "Cabinet", new { orderNumber });
    }
    
    private async Task<PaymentModel?> GetPaymentModel(OrderParams orderParams)
    {
        if (string.IsNullOrWhiteSpace(orderParams.PaymentId) && string.IsNullOrWhiteSpace(orderParams.Link))
            return new PaymentModel() { Price = orderParams.Price, Date = orderParams.Date, IsPaid = false };
        
        if (string.IsNullOrWhiteSpace(orderParams.Date))
            return null;

        orderParams.Date = ConvertPeriodToViewDate(orderParams.Date);
        
        string newPaymentId;
        
        if (string.IsNullOrWhiteSpace(orderParams.PaymentId))
        {
            var linkId = MyRegex().Match(orderParams.Link).Value;
            var linkInfo = await _tinkoff.GetLinkInfo(linkId);
            newPaymentId = linkInfo.OrderId;
                        
            if (string.IsNullOrWhiteSpace(newPaymentId))
                return new PaymentModel() { Price = orderParams.Price, Date = orderParams.Date, IsPaid = false };

            if (orderParams.DataId != null)
                await _data.Update("Links", orderParams.DataId, new Dictionary<string, object>() { ["PaymentId"] = newPaymentId });
            else
            {
                var res = await _data.Create("Links", new Dictionary<string, object>()
                {
                    ["OrderId"] = orderParams.OrderId,
                    ["Link"] = orderParams.Link,
                    ["Date"] = orderParams.Date.TryParseDateTime(out var date) ? date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : orderParams.Date,
                    ["Expiration Date"] = orderParams.Date.TryParseDateTime(out date) ? date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : orderParams.Date,
                    ["Price"] = orderParams.Price,
                    ["PaymentId"] = newPaymentId
                });

                if (!res.Success)
                    return new PaymentModel() { Price = orderParams.Price, Date = orderParams.Date, IsPaid = false };
            }
        }
        else
            newPaymentId = orderParams.PaymentId;
        
        // For testing. TODO: remove this
        if (newPaymentId == "Test")
            return new PaymentModel() { Price = orderParams.Price, Date = orderParams.Date, IsPaid = true };
        
        var result = await _tinkoff.GetPaymentInfo(_config.TinkoffTerminalKey, _config.TinkoffPassword, newPaymentId);
        
        return new PaymentModel() { Price = orderParams.Price, Date = orderParams.Date, IsPaid = result?.Status == OrderStatus.Done };
    }
    
    private async Task<IActionResult?> GetOrCreateLink(OrderParams orderParams, int number, int count)
    {
        var oldDate = orderParams.Date;
        orderParams.Date = await NewDate(orderParams);
        
        (bool success, string result) res;
        
        if (string.IsNullOrWhiteSpace(orderParams.PaymentId) && string.IsNullOrWhiteSpace(orderParams.Link))
        {
            res = await CreateLink(orderParams, oldDate ?? FormatDate(DateTime.Now, "type=3"), number, count);
            
            if (!res.success)
                if (res.result.Contains("RedirectDueDate"))
                    return RedirectToAction(nameof(PaidController.Index), "NotFoundOrder", routeValues: new { message = res.result + $"(Номер сделки: {orderParams.OrderId})" });
            
            return Redirect(res.result);
        }

        if (string.IsNullOrWhiteSpace(orderParams.Date))
            return null;

        orderParams.Date = ConvertPeriodToViewDate(orderParams.Date);
        
        string newPaymentId;

        if (string.IsNullOrWhiteSpace(orderParams.PaymentId))
        {
            var linkId = MyRegex().Match(orderParams.Link).Value;
            var linkInfo = await _tinkoff.GetLinkInfo(linkId);
            newPaymentId = linkInfo.OrderId;

            if (string.IsNullOrWhiteSpace(newPaymentId))
            {
                res = await CreateLink(orderParams, oldDate ?? FormatDate(DateTime.Now, "type=3"), number, count);
            
                if (!res.success)
                    return RedirectToAction(nameof(PaidController.Index), "NotFoundOrder", routeValues: new { message = res.result + $"(Номер сделки: {orderParams.OrderId})" });
            
                return Redirect(res.result);
            }

            if (orderParams.DataId != null)
                await _data.Update("Links", orderParams.DataId, new Dictionary<string, object>() { ["PaymentId"] = newPaymentId });
            else
            {
                res = await _data.Create("Links", new Dictionary<string, object>()
                {
                    ["OrderId"] = orderParams.OrderId,
                    ["Link"] = orderParams.Link,
                    ["Date"] = orderParams.Date.TryParseDateTime(out var date) ? date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : orderParams.Date,
                    ["Expiration Date"] = orderParams.Date.TryParseDateTime(out date) ? date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : orderParams.Date,
                    ["Price"] = orderParams.Price,
                    ["PaymentId"] = newPaymentId
                });

                if (!res.success)
                    return RedirectToAction(nameof(PaidController.Index), "NotFoundOrder", routeValues: new { message = res.result + $"(Номер сделки: {orderParams.OrderId})" });
            }
        }
        else
            newPaymentId = orderParams.PaymentId;
        
        // For testing. TODO: remove this
        if (newPaymentId == "Test")
            return RedirectToAction(nameof(Index), "Cabinet", new { orderNumber = orderParams.OrderId });
        
        var result = await _tinkoff.GetPaymentInfo(_config.TinkoffTerminalKey, _config.TinkoffPassword, newPaymentId);

        if (result?.Status == OrderStatus.Done)
            return RedirectToAction(nameof(Index), "Cabinet", new { orderNumber = orderParams.OrderId });

        if (result?.Status != OrderStatus.Unknown && 
            result?.Status != OrderStatus.Canceled &&
            result?.Status != OrderStatus.TimeLong &&
            !string.IsNullOrWhiteSpace(orderParams.Link)
        )
            return Redirect(orderParams.Link);
        
        res = await CreateLink(orderParams, oldDate ?? FormatDate(DateTime.Now, "type=3"), number, count);
            
        if (!res.success)
            return RedirectToAction(nameof(PaidController.Index), "NotFoundOrder", routeValues: new { message = res.result + $"(Номер сделки: {orderParams.OrderId})" });
            
        return Redirect(res.result);
    }

    private async Task<(bool success, string result)> CreateLink(OrderParams orderParams, string oldDate, int number, int count)
    {
        // Получение данных программы
        var nextId = _main.GetId<string>(orderParams.Record.Fields["Программа"]) ?? string.Empty;
        var record = await _main.GetRecordById("Выезды", nextId);
        
        if (
            string.IsNullOrWhiteSpace(record.Id) || 
            !record.Fields.ContainsKey("Name") || 
            !record.Fields.ContainsKey("Артикул")
        )
            return (false, string.Empty);
        
        var nameProduct = record.Fields.GetString("Name");
        var leadItemCode = record.Fields.GetString("Артикул");

        var text = count == 1 ? $"Предоплата № {number}/{count} за {nameProduct}" : nameProduct;

        // Получение данных клиента
        nextId = _main.GetId<string>(orderParams.Record.Fields["Заказчик"]) ?? string.Empty;
        record = await _main.GetRecordById("Списки", nextId);
        
        if (
            string.IsNullOrWhiteSpace(record.Id) || 
            !record.Fields.ContainsKey("Name") || 
            !record.Fields.ContainsKey("Телефон") ||
            !record.Fields.ContainsKey("Email")
        )
            return (false, string.Empty);
        
        var genLinkParams = new GenerateLinkParams()
        {
            GeneratorLink = _tinkoff,
            Login = _config.TinkoffTerminalKey,
            Password = _config.TinkoffPassword,
            OrderId = orderParams.OrderId,
            PreExpirationDate = !string.IsNullOrWhiteSpace(orderParams.Date) ? orderParams.Date : await NewDate(orderParams),
            NameProduct = text,
            Price = orderParams.Price,
            PaymentMethod = count == 1 ? 1 : 2,
            PaymentObject = count == 1 ? 1 : 10,
            Client = new GenerateLinkClient()
            {
                Name = record.Fields.GetString("Name"), 
                Phone = record.Fields.GetString("Телефон"), 
                Email = record.Fields.GetString("Email")
            },
            LeadItemCode = leadItemCode,
        };
        
        OrderLink? orderLink;
        
        try
        {
            orderLink = await GenerateLinkPay(genLinkParams);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка при создании ссылки на оплату");
            return (false, ex.Message);
        }

        if (string.IsNullOrWhiteSpace(oldDate))
            oldDate = string.IsNullOrWhiteSpace(orderParams.Date) ? DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : orderParams.Date;

        var param = new Dictionary<string, object>()
        {
            ["OrderId"] = orderParams.OrderId,
            ["Link"] = orderLink.FormUrl,
            ["Date"] = oldDate.TryParseDateTime(out var date) ? date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : oldDate,
            ["Expiration Date"] = orderParams.Date.TryParseDateTime(out date) ? date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["Price"] = orderParams.Price,
            ["PaymentId"] = orderLink.OrderId
        };

        if (string.IsNullOrWhiteSpace(orderParams.DataId))
            await _data.Create("Links", param);
        else
            await _data.Update("Links", orderParams.DataId, param);

        if (count <= 0) return (true, orderLink.FormUrl);
        
        var paymentPlan = orderParams.Record.Fields.GetString("Рассрочка");

        if (Regex.IsMatch(paymentPlan, @$"{oldDate}\s{orderParams.Price}\s[^,]+"))
        {
            paymentPlan = Regex.Replace(
                paymentPlan,
                @$"{oldDate}\s{orderParams.Price}\s[^,]+",
                $"{oldDate} {orderParams.Price} {orderLink.OrderId}"
            );
        }
        else
        {
            paymentPlan = paymentPlan.Replace($"{oldDate} {orderParams.Price}",
                $"{oldDate} {orderParams.Price} {orderLink.OrderId}");
        }

        if (!string.IsNullOrWhiteSpace(orderParams.Record.Id))
            await _main.Update("Сделки", orderParams.Record.Id, new Dictionary<string, object>() { { "Рассрочка", paymentPlan } });

        return (true, orderLink.FormUrl);
    }
    
    private async Task<OrderLink> GenerateLinkPay(GenerateLinkParams genLinkParams)
    {
        var fullName = genLinkParams.Client.Name;
        var phone = ConvertToPhone(genLinkParams.Client.Phone);
        var itemCode = genLinkParams.LeadItemCode;
        var expirationDate = ConvertToExpirationDate(genLinkParams.PreExpirationDate);
        
        var email = genLinkParams.Client.Email;
        
        OrderLink? orderLink = default;
        var preOrderNumber = genLinkParams.OrderId;
        var num = 1;

        do
        {
            try
            {
                orderLink = await genLinkParams.GeneratorLink.GenerateOrderLink(new ParamsOrder()
                {
                    Email = email.Replace(" ", "").Replace("\n", ""),
                    Login = genLinkParams.Login,
                    Password = genLinkParams.Password,
                    ExpirationDate = expirationDate,
                    Phone = phone,
                    Fullname = fullName,
                    OrderNumber = genLinkParams.OrderId + " " + Guid.NewGuid().ToString()[..4],
                    PaymentMethod = genLinkParams.PaymentMethod,
                    PaymentObject = genLinkParams.PaymentObject,
                    Product = new Product(genLinkParams.NameProduct, genLinkParams.Price, itemCode)
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при создании ссылки на оплату");
                
                if (
                    ex.Message != "{\"errorCode\":\"1\",\"errorMessage\":\"Заказ с таким номером уже обработан\"}" &&
                    !ex.Message.Contains("Заказ с таким order_id уже существует")
                )
                    throw;
            }

            num++;
            genLinkParams.OrderId = preOrderNumber + " v" + num;
            Thread.Sleep(200);
        } while (orderLink?.FormUrl == null);

        return orderLink;
    }

    [GeneratedRegex(@"[^\/]*$")]
    private static partial Regex MyRegex();
    
    private string ConvertToViewDate(string dateString)
    {
        return dateString.TryParseDateTime(out var date) ? date.ToString("yyyy.MM.dd", _cultureInfo) : dateString;
    }
    
    private string ConvertPeriodToViewDate(string dateString)
    {
        return dateString.TryParseDateTime(out var date) ? date.ToString("dd.MM.yyyy", _cultureInfo) : dateString;
    }
    
    private string ConvertToExpirationDate(string dateString)
    {
        return dateString.TryParseDateTime(out var date) ? date.ToString("yyyy-MM-dd", _cultureInfo) + "T23:55:00" : dateString;
    }
    
    // Получение чисто чисел из номера телефона
    private static string ConvertToPhone(string text) => Regex.Replace(text, @"[^0-9.]", "");
    
    private static string FormatDate(DateTime date, string format) => format switch
    {
        "type=1" => date.ToString("m yyyy", new CultureInfo("ru-RU")),
        "type=2" => date.ToString("dd MM yyyy г.", new CultureInfo("ru-RU")),
        "type=3" => date.ToString("dd.MM.yyyy", new CultureInfo("ru-RU")),
        _ => date.ToString(format)
    };

    private static string TryFormat(string dateString)
    {
        return dateString.TryParseDateTime(out var date) ? FormatDate(date, "type=3") : dateString;
    }

    private async Task<string> NewDate(OrderParams orderParams)
    {
        if (!orderParams.Date.TryParseDateTime(out var date) ||
            (date <= DateTime.Now.AddDays(90) && date >= DateTime.Now))
            return string.IsNullOrWhiteSpace(orderParams.Date) ? FormatDate(DateTime.Now.AddDays(89), "type=3") : orderParams.Date;
        
        var nextId = _main.GetId<string>(orderParams.Record.Fields["Программа"]) ?? string.Empty;
        var record = await _main.GetRecordById("Выезды", nextId);
            
        var newDate = DateTime.Now.AddDays(89);

        if (
            !string.IsNullOrWhiteSpace(record.Id) &&
            record.Fields.ContainsKey("Начало") &&
            record.Fields.GetString("Начало").ParseDateTime() < newDate &&
            record.Fields.GetString("Начало").ParseDateTime() >= DateTime.Now
        )
            newDate = record.Fields.GetString("Начало").ParseDateTime();
            
        return newDate.ToString("dd.MM.yyyy");

    }
}