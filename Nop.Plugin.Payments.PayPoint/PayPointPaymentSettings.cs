using Nop.Core.Configuration;

namespace Nop.Plugin.Payments.PayPoint
{
    public class PayPointPaymentSettings : ISettings
    {
        public string GatewayUrl { get; set; }
        public string MerchantId { get; set; }
        public string RemotePassword { get; set; }
        public string DigestKey { get; set; }
        public decimal AdditionalFee { get; set; }
    }
}
