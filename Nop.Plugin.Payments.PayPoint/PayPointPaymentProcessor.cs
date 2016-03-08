using System;
using System.Collections.Generic;
using System.Globalization;
using System.Web.Routing;
using Nop.Core;
using Nop.Core.Domain.Directory;
using Nop.Core.Domain.Orders;
using Nop.Core.Domain.Payments;
using Nop.Core.Plugins;
using Nop.Plugin.Payments.PayPoint.Controllers;
using Nop.Services.Configuration;
using Nop.Services.Directory;
using Nop.Services.Localization;
using Nop.Services.Payments;
using Nop.Web.Framework;

namespace Nop.Plugin.Payments.PayPoint
{
    /// <summary>
    /// PayPoint payment processor
    /// </summary>
    public class PayPointPaymentProcessor : BasePlugin, IPaymentMethod
    {
        #region Fields

        private readonly PayPointPaymentSettings _payPointPaymentSettings;
        private readonly ICurrencyService _currencyService;
        private readonly CurrencySettings _currencySettings;
        private readonly ISettingService _settingService;
        private readonly IWebHelper _webHelper;

        #endregion

        #region Ctor

        public PayPointPaymentProcessor(PayPointPaymentSettings payPointPaymentSettings,
            ICurrencyService currencyService, CurrencySettings currencySettings,
            ISettingService settingService, IWebHelper webHelper)
        {
            this._payPointPaymentSettings = payPointPaymentSettings;
            this._currencyService = currencyService;
            this._currencySettings = currencySettings;
            this._settingService = settingService;
            this._webHelper = webHelper;
        }

        #endregion
        
        #region Methods

        /// <summary>
        /// Process a payment
        /// </summary>
        /// <param name="processPaymentRequest">Payment info required for an order processing</param>
        /// <returns>Process payment result</returns>
        public ProcessPaymentResult ProcessPayment(ProcessPaymentRequest processPaymentRequest)
        {
            var result = new ProcessPaymentResult();
            result.NewPaymentStatus = PaymentStatus.Pending;
            return result;
        }

        /// <summary>
        /// Post process payment (used by payment gateways that require redirecting to a third-party URL)
        /// </summary>
        /// <param name="postProcessPaymentRequest">Payment info required for an order processing</param>
        public void PostProcessPayment(PostProcessPaymentRequest postProcessPaymentRequest)
        {
            var gatewayUrl = new Uri(_payPointPaymentSettings.GatewayUrl);
            var post = new RemotePost();
            post.FormName = "PayPoint";
            post.Url = gatewayUrl.ToString();
            post.Add("merchant", _payPointPaymentSettings.MerchantId);
            post.Add("trans_id", postProcessPaymentRequest.Order.Id.ToString());
            post.Add("currency", _currencyService.GetCurrencyById(_currencySettings.PrimaryStoreCurrencyId).CurrencyCode);
            post.Add("amount", String.Format(CultureInfo.InvariantCulture, "{0:0.00}", postProcessPaymentRequest.Order.OrderTotal));
            post.Add("callback", String.Format("{0}Plugins/PaymentPayPoint/Return", _webHelper.GetStoreLocation(false)));
            post.Add("digest", PayPointHelper.CalcRequestSign(post, _payPointPaymentSettings.RemotePassword));
            //uncomment the line below if you want to test the system
            //post.Add("test_status", "true");
            post.Post();
        }

        /// <summary>
        /// Returns a value indicating whether payment method should be hidden during checkout
        /// </summary>
        /// <param name="cart">Shoping cart</param>
        /// <returns>true - hide; false - display.</returns>
        public bool HidePaymentMethod(IList<ShoppingCartItem> cart)
        {
            //you can put any logic here
            //for example, hide this payment method if all products in the cart are downloadable
            //or hide this payment method if current customer is from certain country
            return false;
        }

        /// <summary>
        /// Gets additional handling fee
        /// </summary>
        /// <param name="cart">Shoping cart</param>
        /// <returns>Additional handling fee</returns>
        public decimal GetAdditionalHandlingFee(IList<ShoppingCartItem> cart)
        {
            return _payPointPaymentSettings.AdditionalFee;
        }

        /// <summary>
        /// Captures payment
        /// </summary>
        /// <param name="capturePaymentRequest">Capture payment request</param>
        /// <returns>Capture payment result</returns>
        public CapturePaymentResult Capture(CapturePaymentRequest capturePaymentRequest)
        {
            var result = new CapturePaymentResult();
            result.AddError("Capture method not supported");
            return result;
        }

        /// <summary>
        /// Refunds a payment
        /// </summary>
        /// <param name="refundPaymentRequest">Request</param>
        /// <returns>Result</returns>
        public RefundPaymentResult Refund(RefundPaymentRequest refundPaymentRequest)
        {
            var result = new RefundPaymentResult();
            result.AddError("Refund method not supported");
            return result;
        }

        /// <summary>
        /// Voids a payment
        /// </summary>
        /// <param name="voidPaymentRequest">Request</param>
        /// <returns>Result</returns>
        public VoidPaymentResult Void(VoidPaymentRequest voidPaymentRequest)
        {
            var result = new VoidPaymentResult();
            result.AddError("Void method not supported");
            return result;
        }

        /// <summary>
        /// Process recurring payment
        /// </summary>
        /// <param name="processPaymentRequest">Payment info required for an order processing</param>
        /// <returns>Process payment result</returns>
        public ProcessPaymentResult ProcessRecurringPayment(ProcessPaymentRequest processPaymentRequest)
        {
            var result = new ProcessPaymentResult();
            result.AddError("Recurring payment not supported");
            return result;
        }

        /// <summary>
        /// Cancels a recurring payment
        /// </summary>
        /// <param name="cancelPaymentRequest">Request</param>
        /// <returns>Result</returns>
        public CancelRecurringPaymentResult CancelRecurringPayment(CancelRecurringPaymentRequest cancelPaymentRequest)
        {
            var result = new CancelRecurringPaymentResult();
            result.AddError("Recurring payment not supported");
            return result;
        }

        /// <summary>
        /// Gets a value indicating whether customers can complete a payment after order is placed but not completed (for redirection payment methods)
        /// </summary>
        /// <param name="order">Order</param>
        /// <returns>Result</returns>
        public bool CanRePostProcessPayment(Order order)
        {
            if (order == null)
                throw new ArgumentNullException("order");

            //PayPoint is the redirection payment method
            //It also validates whether order is also paid (after redirection) so customers will not be able to pay twice
            
            //payment status should be Pending
            if (order.PaymentStatus != PaymentStatus.Pending)
                return false;

            //let's ensure that at least 1 minute passed after order is placed
            if ((DateTime.UtcNow - order.CreatedOnUtc).TotalMinutes < 1)
                return false;

            return true;
        }

        /// <summary>
        /// Gets a route for provider configuration
        /// </summary>
        /// <param name="actionName">Action name</param>
        /// <param name="controllerName">Controller name</param>
        /// <param name="routeValues">Route values</param>
        public void GetConfigurationRoute(out string actionName, out string controllerName, out RouteValueDictionary routeValues)
        {
            actionName = "Configure";
            controllerName = "PaymentPayPoint";
            routeValues = new RouteValueDictionary() { { "Namespaces", "Nop.Plugin.Payments.PayPoint.Controllers" }, { "area", null } };
        }

        /// <summary>
        /// Gets a route for payment info
        /// </summary>
        /// <param name="actionName">Action name</param>
        /// <param name="controllerName">Controller name</param>
        /// <param name="routeValues">Route values</param>
        public void GetPaymentInfoRoute(out string actionName, out string controllerName, out RouteValueDictionary routeValues)
        {
            actionName = "PaymentInfo";
            controllerName = "PaymentPayPoint";
            routeValues = new RouteValueDictionary() { { "Namespaces", "Nop.Plugin.Payments.PayPoint.Controllers" }, { "area", null } };
        }

        public Type GetControllerType()
        {
            return typeof(PaymentPayPointController);
        }

        public override void Install()
        {
            var settings = new PayPointPaymentSettings()
            {
                GatewayUrl = "https://www.secpay.com/java-bin/ValCard",
                MerchantId = "",
                RemotePassword = "",
                DigestKey = "",
                AdditionalFee = 0,
            };
            _settingService.SaveSetting(settings);

            //locales
            this.AddOrUpdatePluginLocaleResource("Plugins.Payments.PayPoint.RedirectionTip", "You will be redirected to PayPoint site to complete the order.");
            this.AddOrUpdatePluginLocaleResource("Plugins.Payments.PayPoint.GatewayUrl", "Gateway URL");
            this.AddOrUpdatePluginLocaleResource("Plugins.Payments.PayPoint.GatewayUrl.Hint", "Enter gateway URL.");
            this.AddOrUpdatePluginLocaleResource("Plugins.Payments.PayPoint.MerchantId", "Merchant ID");
            this.AddOrUpdatePluginLocaleResource("Plugins.Payments.PayPoint.MerchantId.Hint", "Enter merchant ID.");
            this.AddOrUpdatePluginLocaleResource("Plugins.Payments.PayPoint.RemotePassword", "Remote password");
            this.AddOrUpdatePluginLocaleResource("Plugins.Payments.PayPoint.RemotePassword.Hint", "Enter remote password.");
            this.AddOrUpdatePluginLocaleResource("Plugins.Payments.PayPoint.DigestKey", "Digest key");
            this.AddOrUpdatePluginLocaleResource("Plugins.Payments.PayPoint.DigestKey.Hint", "Enter figest key.");
            this.AddOrUpdatePluginLocaleResource("Plugins.Payments.PayPoint.AdditionalFee", "Additional fee");
            this.AddOrUpdatePluginLocaleResource("Plugins.Payments.PayPoint.AdditionalFee.Hint", "Enter additional fee to charge your customers.");
            
            base.Install();
        }

        public override void Uninstall()
        {
            //locales
            this.DeletePluginLocaleResource("Plugins.Payments.PayPoint.RedirectionTip");
            this.DeletePluginLocaleResource("Plugins.Payments.PayPoint.GatewayUrl");
            this.DeletePluginLocaleResource("Plugins.Payments.PayPoint.GatewayUrl.Hint");
            this.DeletePluginLocaleResource("Plugins.Payments.PayPoint.MerchantId");
            this.DeletePluginLocaleResource("Plugins.Payments.PayPoint.MerchantId.Hint");
            this.DeletePluginLocaleResource("Plugins.Payments.PayPoint.RemotePassword");
            this.DeletePluginLocaleResource("Plugins.Payments.PayPoint.RemotePassword.Hint");
            this.DeletePluginLocaleResource("Plugins.Payments.PayPoint.DigestKey");
            this.DeletePluginLocaleResource("Plugins.Payments.PayPoint.DigestKey.Hint");
            this.DeletePluginLocaleResource("Plugins.Payments.PayPoint.AdditionalFee");
            this.DeletePluginLocaleResource("Plugins.Payments.PayPoint.AdditionalFee.Hint");
            

            base.Uninstall();
        }
        #endregion

        #region Properies

        /// <summary>
        /// Gets a value indicating whether capture is supported
        /// </summary>
        public bool SupportCapture
        {
            get
            {
                return false;
            }
        }

        /// <summary>
        /// Gets a value indicating whether partial refund is supported
        /// </summary>
        public bool SupportPartiallyRefund
        {
            get
            {
                return false;
            }
        }

        /// <summary>
        /// Gets a value indicating whether refund is supported
        /// </summary>
        public bool SupportRefund
        {
            get
            {
                return false;
            }
        }

        /// <summary>
        /// Gets a value indicating whether void is supported
        /// </summary>
        public bool SupportVoid
        {
            get
            {
                return false;
            }
        }

        /// <summary>
        /// Gets a recurring payment type of payment method
        /// </summary>
        public RecurringPaymentType RecurringPaymentType
        {
            get
            {
                return RecurringPaymentType.NotSupported;
            }
        }

        /// <summary>
        /// Gets a payment method type
        /// </summary>
        public PaymentMethodType PaymentMethodType
        {
            get
            {
                return PaymentMethodType.Redirection;
            }
        }

        /// <summary>
        /// Gets a value indicating whether we should display a payment information page for this plugin
        /// </summary>
        public bool SkipPaymentInfo
        {
            get { return false; }
        }

        #endregion
    }
}
