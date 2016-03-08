using System.Collections.Generic;
using System.Web.Mvc;
using Nop.Core;
using Nop.Core.Domain.Payments;
using Nop.Plugin.Payments.PayPoint.Models;
using Nop.Services.Configuration;
using Nop.Services.Logging;
using Nop.Services.Orders;
using Nop.Services.Payments;
using Nop.Web.Framework.Controllers;

namespace Nop.Plugin.Payments.PayPoint.Controllers
{
    public class PaymentPayPointController : BasePaymentController
    {
        private readonly ISettingService _settingService;
        private readonly IPaymentService _paymentService;
        private readonly IOrderService _orderService;
        private readonly IOrderProcessingService _orderProcessingService;
        private readonly IWebHelper _webHelper;
        private readonly PayPointPaymentSettings _payPointPaymentSettings;
        private readonly PaymentSettings _paymentSettings;
        private readonly IWorkContext _workContext;

        public PaymentPayPointController(ISettingService settingService, 
            IPaymentService paymentService, IOrderService orderService, 
            IOrderProcessingService orderProcessingService, IWebHelper webHelper,
            PayPointPaymentSettings payPointPaymentSettings,
            PaymentSettings paymentSettings, IWorkContext workContext)
        {
            this._settingService = settingService;
            this._paymentService = paymentService;
            this._orderService = orderService;
            this._orderProcessingService = orderProcessingService;
            this._webHelper = webHelper;
            this._payPointPaymentSettings = payPointPaymentSettings;
            this._paymentSettings = paymentSettings;
            this._workContext = workContext;
        }
        
        [AdminAuthorize]
        [ChildActionOnly]
        public ActionResult Configure()
        {
            var model = new ConfigurationModel();
            model.GatewayUrl = _payPointPaymentSettings.GatewayUrl;
            model.MerchantId = _payPointPaymentSettings.MerchantId;
            model.RemotePassword = _payPointPaymentSettings.RemotePassword;
            model.DigestKey = _payPointPaymentSettings.DigestKey;
            model.AdditionalFee = _payPointPaymentSettings.AdditionalFee;

            return View("~/Plugins/Payments.PayPoint/Views/PaymentPayPoint/Configure.cshtml", model);
        }

        [HttpPost]
        [AdminAuthorize]
        [ChildActionOnly]
        public ActionResult Configure(ConfigurationModel model)
        {
            if (!ModelState.IsValid)
                return Configure();

            //save settings
            _payPointPaymentSettings.GatewayUrl = model.GatewayUrl;
            _payPointPaymentSettings.MerchantId = model.MerchantId;
            _payPointPaymentSettings.RemotePassword = model.RemotePassword;
            _payPointPaymentSettings.DigestKey = model.DigestKey;
            _payPointPaymentSettings.AdditionalFee = model.AdditionalFee;
            _settingService.SaveSetting(_payPointPaymentSettings);

            return View("~/Plugins/Payments.PayPoint/Views/PaymentPayPoint/Configure.cshtml", model);
        }

        [ChildActionOnly]
        public ActionResult PaymentInfo()
        {
            var model = new PaymentInfoModel();
            return View("~/Plugins/Payments.PayPoint/Views/PaymentPayPoint/PaymentInfo.cshtml", model);
        }

        [NonAction]
        public override IList<string> ValidatePaymentForm(FormCollection form)
        {
            var warnings = new List<string>();
            return warnings;
        }

        [NonAction]
        public override ProcessPaymentRequest GetPaymentInfo(FormCollection form)
        {
            var paymentInfo = new ProcessPaymentRequest();
            return paymentInfo;
        }

        [ValidateInput(false)]
        public ActionResult Return()
        {
            var processor = _paymentService.LoadPaymentMethodBySystemName("Payments.PayPoint") as PayPointPaymentProcessor;
            if (processor == null ||
                !processor.IsPaymentMethodActive(_paymentSettings) || !processor.PluginDescriptor.Installed)
                throw new NopException("PayPoint module cannot be loaded");


            //PayPoint retrieves the HTML from the page and renders it to the user from the SecPay.com domain
            //that's why we do not do any redirects here

            //'Content' has to be contained in an HTML document for PayPoint to accept it 

            if (!PayPointHelper.ValidateResponseSign(Request.Url, _payPointPaymentSettings.DigestKey))
            {
                return Content("<html><body><p>nopCommerce. Cannot validate response sign</p></body></html>");
            }
            if (!_webHelper.QueryString<bool>("valid"))
            {
                return Content("<html><body><p>nopCommerce. valid parameter is not true</p></body></html>");
            }

            var orderId = _webHelper.QueryString<int>("trans_id");
            var order = _orderService.GetOrderById(orderId);
            if (order == null)
            {
                return Content("<html><body><p>nopCommerce. Order cannot be loaded</p></body></html>");
            }

            if (_orderProcessingService.CanMarkOrderAsPaid(order))
            {
                _orderProcessingService.MarkOrderAsPaid(order);
            }
            return Content("<html><body><p>Your order has been paid</p></body></html>");
        }
    }
}