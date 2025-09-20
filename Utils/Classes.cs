namespace ALab_Cabinet.Utils;

public class OrderLink
{
    public string OrderId { get; set; }
    public string FormUrl { get; set; }
}

public class OrderInfo
{
    public string PaymentLink { get; set; }
    public string OrderId { get; set; }
    public string Date { get; set; }
    public string Amount { get; set; }
    public string? Message { get; set; }
    public OrderStatus Status { get; set; }
}

public class ParamsOrder
{
    public string Login { get; set; }
    public string Password { get; set; }
    public string OrderNumber { get; set; }
    public string Fullname { get; set; }
    public string Email { get; set; }
    public string Phone { get; set; }
    public string ExpirationDate { get; set; }
    public Product Product { get; set; }
    public int PaymentMethod { get; set; }
    public int PaymentObject { get; set; }
}

public class Product
{
    /// <summary>
    /// Наименование или описание товарной позиции в свободной форме
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    /// Количество товара
    /// </summary>
    public int QuantityValue { get; set; }

    /// <summary>
    /// Мера измерения (шт.)
    /// </summary>
    public string QuantityMeasure { get; set; }

    /// <summary>
    /// Цена одной ед. товара
    /// </summary>
    public string ItemPrice { get; set; }

    /// <summary>
    /// Сумма стоимости всех товарных позиций одного positionId
    /// </summary>
    public string ItemAmount { get; set; }

    /// <summary>
    /// Номер (идентификатор) товарной позиции в системе магазина
    /// </summary>
    public string ItemCode { get; set; }


    /// <summary>
    /// Создание нового продукта
    /// </summary>
    /// <param name="name"></param>
    /// <param name="itemPrice">Сумма в рублях</param>
    /// <param name="itemCode">Идентификатор товара</param>
    public Product(string name, int itemPrice, string itemCode)
    {
        Name = name;
        QuantityValue = 1;
        QuantityMeasure = "шт.";
        ItemPrice = itemPrice.ToString() + "00";
        ItemCode = itemCode;
        ItemAmount = (QuantityValue * int.Parse(ItemPrice)).ToString();
    }
}

public class TinkoffConf
{
    public string Url { get; set; }
    public string ReturnUrl { get; set; }
    public string FailUrl { get; set; }
    public string TaxSystem { get; set; }
    public string TaxType { get; set; }
}

public class LinkAllInfoTinkoff
{
    public LinkInfoTinkoff Transaction { get; set; }
}

public class LinkInfoTinkoff
{
    public string Status { get; set; }
    public int AttemptCount { get; set; }
    public string Description { get; set; }
    public string OrderId { get; set; }
    public string MerchantName { get; set; }
    public int Amount { get; set; }
    public string PaymentId { get; set; }
}

public class OrderInfoTinkoff
{
    public string TerminalKey { get; set; }
    public int Amount { get; set; }
    public string OrderId { get; set; }
    public bool Success { get; set; }
    public string Status { get; set; }
    public string PaymentId { get; set; }
    public int ErrorCode { get; set; }
    public string Message { get; set; }
    public string Details { get; set; }
}

public class OrderLinkTinkoff
{
    public bool Success { get; set; }
    public int ErrorCode { get; set; }
    public string TerminalKey { get; set; }
    public string Status { get; set; }
    public string PaymentId { get; set; }
    public string OrderId { get; set; }
    public int Amount { get; set; }
    public string PaymentURL { get; set; }
    public string Message { get; set; }
    public string Details { get; set; }
}

public class OrderParams
{
    public string? Date { get; set; }
    public int Price { get; set; }
    public string? PaymentId { get; set; }
    public string? Link { get; set; }
    public string? DataId { get; set; }
    public string OrderId { get; set; }
    public (Dictionary<string,object> Fields, string Id) Record { get; set; }
}

public class GenerateLinkParams
{
    public IGeneratorLink GeneratorLink { get; set; }
    public string Login { get; set; }
    public string Password { get; set; } 
    public string OrderId { get; set; }
    public string PreExpirationDate { get; set; }
    public string NameProduct { get; set; }
    public int Price { get; set; }
    public int PaymentMethod { get; set; }
    public int PaymentObject { get; set; }
    public GenerateLinkClient Client { get; set; }
    public string LeadItemCode { get; set; }
}

public class GenerateLinkClient
{
    public string Name { get; set; }
    public string Phone { get; set; }
    public string Email { get; set; }
}

public class ExpiresUserOrder
{
    public string OrderId { get; set; }
    public string? UserLink { get; set; }
    public string Date { get; set; }
    public string Amount { get; set; }
    public string? Link { get; set; }
}

public class CheckAllParams
{
    public IDatabase Db { get; set; }
    public IDatabase Data { get; set; }
    public IGeneratorLink Generator { get; set; }
    public string LoginGen { get; set; }
    public string PasswordGen { get; set; }
    public Config Config { get; set; }
    public ManagerObject<List<ExpiresUserOrder>>? ManagerPeriods { get; set; }
}