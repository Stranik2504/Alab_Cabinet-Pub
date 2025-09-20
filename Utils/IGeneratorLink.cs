namespace ALab_Cabinet.Utils;

public interface IGeneratorLink
{
    Task<OrderLink?> GenerateOrderLink(ParamsOrder paramsOrder);
    Task<OrderInfo?> GetPaymentInfo(string login, string password, string orderId);
}