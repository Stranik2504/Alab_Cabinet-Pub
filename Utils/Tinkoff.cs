using System.Diagnostics;
using System.Globalization;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;

namespace ALab_Cabinet.Utils;

public class Tinkoff : IGeneratorLink
{
    private TinkoffConf? Config => _config?.Obj;

    private readonly ManagerObject<TinkoffConf> _config;
    private readonly ILogger? _logger;

    private static readonly Dictionary<int, string> PaymentMethod = new()
    {
        { 1, "lfull_prepayment" },
        { 2, "lprepayment" },
        { 3, "ladvance" },
        { 4, "lfull_payment" },
        { 5, "lpartial_payment" },
        { 6, "lcredit" },
        { 7, "lcredit_payment" }
    };

    private static readonly Dictionary<int, string> PaymentObject = new()
    {
        { 1, "commodity" },
        { 2, "excise" },
        { 3, "job" },
        { 4, "service" },
        { 5, "gambling_bet" },
        { 6, "gambling_prize" },
        { 7, "lottery" },
        { 8, "lottery_prize" },
        { 9, "intellectual_activity" },
        { 10, "payment" },
        { 11, "agent_commission" },
        { 12, "composite" },
        { 13, "another" }
    }; 

    public Tinkoff(string configPath, ILogger<Tinkoff>? logger)
    {
        _config = new ManagerObject<TinkoffConf>(configPath);
        _config.Load();

        _logger = logger;
    }

    public async Task<OrderLink?> GenerateOrderLink(ParamsOrder paramsOrder)
    {
        _logger?.LogInformation("Start generate order link to order: {0}", paramsOrder.OrderNumber);
        
        if (paramsOrder.ExpirationDate.TryParseDateTime(out var date))
            paramsOrder.ExpirationDate = date.ToString("yyyy-MM-ddTHH:mm:ss+03:00", CultureInfo.InvariantCulture);
        else
            paramsOrder.ExpirationDate += "+03:00";
        
        var reqParams = new Dictionary<string, dynamic>
        {
            {"TerminalKey", paramsOrder.Login}, 
            {"Amount", ulong.Parse(paramsOrder.Product.ItemAmount) },
            {"OrderId", paramsOrder.OrderNumber},
            {"Description", paramsOrder.Product.Name[..Math.Min(paramsOrder.Product.Name.Length, 247)] + (paramsOrder.Product.Name.Length > 247 ? "..." : "")},
            {"SuccessURL", Config?.ReturnUrl ?? string.Empty},
            {"FailURL", Config?.FailUrl ?? string.Empty},
            {"RedirectDueDate", paramsOrder.ExpirationDate }
        };

        var listParams = reqParams.ToList().Select(x => (x.Key, x.Value)).ToList();
        listParams.Add(("Password", paramsOrder.Password));
        listParams.Sort((x, y) => string.Compare(x.Key, y.Key, StringComparison.Ordinal));
        
        var tokenString = "";
        listParams.Select(x => x.Value).ForEach(x => tokenString += x);
        
        var token = HashString(tokenString);
        
        // Add params with nested data
        reqParams.Add("Token", token);
        reqParams.Add("DATA", new Dictionary<string, string>() { { "Phone", paramsOrder.Phone } });
        reqParams.Add("Receipt", new Dictionary<string, dynamic>()
        {
            // User information
            {"Email", paramsOrder.Email},
            {"Phone", paramsOrder.Phone},
            // Taxation
            {"Taxation", Config?.TaxSystem ?? string.Empty},
            // Payment
            {
                "Items", new List<dynamic>()
                {
                    new Dictionary<string, dynamic>()
                    {
                        {"Name", paramsOrder.Product.Name },
                        {"Price", uint.Parse(paramsOrder.Product.ItemPrice) },
                        {"Quantity", paramsOrder.Product.QuantityValue},
                        {"MeasurementUnit", paramsOrder.Product.QuantityMeasure},
                        {"Amount", uint.Parse(paramsOrder.Product.ItemAmount) },
                        {"PaymentMethod", PaymentMethod[paramsOrder.PaymentMethod]},
                        {"PaymentObject", PaymentObject[paramsOrder.PaymentObject]},
                        {"Tax", Config?.TaxType ?? string.Empty},
                            
                    }
                }
            }
        });
        
        _logger?.LogInformation("The end of collecting all parameters");
        
        using var client = CreateClient();
        
        using var content = new StringContent(JsonConvert.SerializeObject(reqParams));
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        
        _logger?.LogInformation("Created client and content for request");
        
        var response = await client.PostAsync($"https://{Config?.Url}/Init", content);
        _logger?.LogInformation("Make request to Tinkoff to {0}", $"https://{Config?.Url}/Init");
        var orderTinkoff = await VaildateAndReadAnswer<OrderLinkTinkoff>(response);
        
        _logger?.LogInformation("Get response from Tinkoff");

        if (orderTinkoff.ErrorCode != 0)
        {
            // 20 - Ошибка повторного идентификатора заказа.
            if (orderTinkoff.ErrorCode != 20)
                _logger?.LogWarning("Error when trying to generate a link again in Tinkoff: {0}, {0}", orderTinkoff, paramsOrder);
            
            _logger?.LogError("Error when trying to generate a link in Tinkoff: {0}", orderTinkoff.Details);
            throw new Exception(orderTinkoff.Details);
        }

        _logger?.LogInformation("End generate order link to order: {0} (successfully)", paramsOrder.OrderNumber);
        
        return new OrderLink()
        {
            OrderId = orderTinkoff.PaymentId, 
            FormUrl = orderTinkoff.PaymentURL
        };
    }

    public async Task<OrderInfo?> GetPaymentInfo(string login, string password, string paymentId)
    {
        if (string.IsNullOrWhiteSpace(paymentId))
        {
            _logger?.LogInformation("PaymentId is empty in GetPaymentInfo");
            return default;
        }
        
        _logger?.LogInformation("Start get information about paymentId: {0}", paymentId);

        var reqParams = new Dictionary<string, dynamic>
        {
            {"TerminalKey", login}, 
            {"PaymentId", paymentId}
        };
        
        var listParams = reqParams.ToList().Select(x => (x.Key, x.Value)).ToList();
        listParams.Add(("Password", password));
        listParams.Sort((x, y) => string.Compare(x.Key, y.Key, StringComparison.Ordinal));
        
        var tokenString = "";
        listParams.Select(x => x.Value).ForEach(x => tokenString += x);
        
        var token = HashString(tokenString);
        reqParams.Add("Token", token);
        
        _logger?.LogInformation("The end of collecting all parameters");
        
        using var client = CreateClient();
        
        using var content = new StringContent(JsonConvert.SerializeObject(reqParams));
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        
        _logger?.LogInformation("Created client and content for request");
        
        var response = await client.PostAsync($"https://{Config?.Url}/GetState", content);
        _logger?.LogInformation("Make request to Tinkoff to {0}", $"https://{Config?.Url}/GetState");
        var orderTinkoff = await VaildateAndReadAnswer<OrderInfoTinkoff>(response);
        
        _logger?.LogInformation("Get response from Tinkoff");

        if (orderTinkoff.ErrorCode == 0)
            return new OrderInfo()
            {
                Amount = (orderTinkoff.Amount / 100).ToString(),
                OrderId = orderTinkoff.PaymentId,
                PaymentLink = string.Empty,
                Message = orderTinkoff.Message,
                Status = ParseStatus(orderTinkoff.Status)
            };
        
        _logger?.LogError("Error when trying to generate a link in Tinkoff: {0}", orderTinkoff);
        throw new Exception(orderTinkoff.Details);

    }
    
    // TODO Think how make it from api
    public async Task<OrderInfo> GetLinkInfo(string orderId)
    {
        if (string.IsNullOrWhiteSpace(orderId))
        {
            _logger?.LogInformation("PaymentId is empty in GetLinkInfo");
            return new OrderInfo();
        }
        
        _logger?.LogInformation("Start get information about orderId(GetLinkInfo): {0}", orderId);

        using var client = CreateClient();
        _logger?.LogInformation("Created client and content for request");
        var response = await client.GetAsync($"https://securepay.tinkoff.ru/platform/api/v1/pf/session/" + orderId);
        _logger?.LogInformation("Make get request to Tinkoff to {0}", $"https://securepay.tinkoff.ru/platform/api/v1/pf/session/" + orderId);
        var orderTinkoff = await VaildateAndReadAnswer<LinkAllInfoTinkoff>(response);
        
        _logger?.LogInformation("Get response from Tinkoff: {0}", orderTinkoff);
        
        return new OrderInfo()
        {
            Amount = (orderTinkoff.Transaction.Amount / 100).ToString(),
            OrderId = orderTinkoff.Transaction.PaymentId,
            PaymentLink = string.Empty,
            Status = ParseStatus(orderTinkoff.Transaction.Status)
        };
    }

    private static OrderStatus ParseStatus(string status) =>
        status switch
        {
            "NEW" => OrderStatus.New,
            "FORM_SHOWED" => OrderStatus.InProcess,
            "CONFIRMED" => OrderStatus.Done,
            "CANCELED" => OrderStatus.Canceled,
            "REJECTED" => OrderStatus.Canceled,
            "AUTH_FAIL" => OrderStatus.AuthFail,
            _ => OrderStatus.Unknown
        };
    
    private static string HashString(string str)
    {
        using var sha256 = SHA256.Create();
        var bytes = Encoding.UTF8.GetBytes(str);
        var hash = sha256.ComputeHash(bytes);
        return BitConverter.ToString(hash).Replace("-", "").ToLower();
    }

    private HttpClient CreateClient()
    {
        _logger?.LogInformation("Start create Client");
        
        var handler = new HttpClientHandler();
        handler.AllowAutoRedirect = true;
        handler.ClientCertificateOptions = ClientCertificateOption.Manual;
        handler.ServerCertificateCustomValidationCallback = (_, _, _, _) => true;

        var client = new HttpClient(handler);
        
        client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/117.0.5938.0 Safari/537.36");

        _logger?.LogInformation("End create Client");
        
        return client;
    }

    private async Task<T> VaildateAndReadAnswer<T>(HttpResponseMessage? response)
    {
        _logger?.LogInformation("Validate response");

        if (response == null)
        {
            _logger?.LogError("Response from tinkoff is null");
            throw new Exception("Response to tinkoff is null");
        }

        if (!response.IsSuccessStatusCode)
        {
            _logger?.LogError("Status code of response is not success: {0}", response);
            throw new Exception("Error when trying to send a request to tinkoff");
        }
        
        var stream = await response.Content.ReadAsStreamAsync();
        using var reader = new StreamReader(stream);
        var text = await reader.ReadToEndAsync();
        
        _logger?.LogInformation("Read response from Tinkoff: {0}", text);

        var result = JsonConvert.DeserializeObject<T>(text);
        
        _logger?.LogInformation("Deserialize response from Tinkoff: {0}", result);

        if (result == null)
        {
            _logger?.LogError("Error when trying to recognize the received data to generate a link in Tinkoff");
            throw new Exception("Error when trying to recognize the received data to generate a link in Tinkoff");
        }
        
        _logger?.LogInformation("End validate response");

        return result;
    }
}