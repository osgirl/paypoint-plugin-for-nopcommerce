using System.ComponentModel;
using Nop.Web.Framework;
using Nop.Web.Framework.Mvc;

namespace Nop.Plugin.Payments.PayPoint.Models
{
    public class ConfigurationModel : BaseNopModel
    {
        [NopResourceDisplayName("Plugins.Payments.PayPoint.GatewayUrl")]
        public string GatewayUrl { get; set; }

        [NopResourceDisplayName("Plugins.Payments.PayPoint.MerchantId")]
        public string MerchantId { get; set; }

        [NopResourceDisplayName("Plugins.Payments.PayPoint.RemotePassword")]
        public string RemotePassword { get; set; }

        [NopResourceDisplayName("Plugins.Payments.PayPoint.DigestKey")]
        public string DigestKey { get; set; }

        [NopResourceDisplayName("Plugins.Payments.PayPoint.AdditionalFee")]
        public decimal AdditionalFee { get; set; }
    }
}