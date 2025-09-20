namespace ALab_Cabinet.Models;

public class PaymentsModel
{
    public List<PaymentModel> Payments { get; set; }
    public int Sum { get; set; }
    public string OrderId { get; set; }
}