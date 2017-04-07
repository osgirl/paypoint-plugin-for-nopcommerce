using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Web;
using System.Web.Routing;
using Newtonsoft.Json;
using Nop.Core;
using Nop.Core.Domain.Directory;
using Nop.Core.Domain.Orders;
using Nop.Core.Domain.Payments;
using Nop.Core.Plugins;
using Nop.Plugin.Payments.PayPoint.Controllers;
using Nop.Services.Configuration;
using Nop.Services.Directory;
using Nop.Services.Localization;
using Nop.Services.Logging;
using Nop.Services.Orders;
using Nop.Services.Payments;

namespace Nop.Plugin.Payments.PayPoint
{
    /// <summary>
    /// PayPoint payment processor
    /// </summary>
    public class PayPointPaymentProcessor : BasePlugin, IPaymentMethod
    {
        #region Fields

        private readonly CurrencySettings _currencySettings;
        private readonly HttpContextBase _httpContext;
        private readonly ICurrencyService _currencyService;
        private readonly ILogger _logger;
        private readonly IOrderTotalCalculationService _orderTotalCalculationService;
        private readonly ISettingService _settingService;
        private readonly IWebHelper _webHelper;
        private readonly IWorkContext _workContext;
        private readonly PayPointPaymentSettings _payPointPaymentSettings;
        private readonly ILocalizationService _localizationService;

        #endregion

        #region Ctor

        public PayPointPaymentProcessor(CurrencySettings currencySettings,
            HttpContextBase httpContext,
            ICurrencyService currencyService,
            ILogger logger,
            IOrderTotalCalculationService orderTotalCalculationService,
            ISettingService settingService,
            IWebHelper webHelper,
            IWorkContext workContext,
            PayPointPaymentSettings payPointPaymentSettings,
            ILocalizationService localizationService)
        {
            this._currencySettings = currencySettings;
            this._httpContext = httpContext;
            this._currencyService = currencyService;
            this._logger = logger;
            this._orderTotalCalculationService = orderTotalCalculationService;
            this._settingService = settingService;
            this._webHelper = webHelper;
            this._workContext = workContext;
            this._payPointPaymentSettings = payPointPaymentSettings;
            this._localizationService = localizationService;
        }

        #endregion

        #region Utilities

        /// <summary>
        /// Post request to PayPoint API
        /// </summary>
        /// <param name="payPointPayment">PayPoint payment information</param>
        /// <returns>PayPoint payment response</returns>
        protected PayPointPaymentResponse PostRequest(PayPointPayment payPointPayment)
        {
            var postData = Encoding.Default.GetBytes(JsonConvert.SerializeObject(payPointPayment));
            var serviceUrl = _payPointPaymentSettings.UseSandbox ? "https://api.mite.pay360.com" : "https://api.pay360.com";
            var login = string.Format("{0}:{1}", _payPointPaymentSettings.ApiUsername, _payPointPaymentSettings.ApiPassword);
            var authorization = Convert.ToBase64String(Encoding.Default.GetBytes(login));
            var request = (HttpWebRequest)WebRequest.Create(string.Format("{0}/hosted/rest/sessions/{1}/payments", serviceUrl, _payPointPaymentSettings.InstallationId));
            request.Headers.Add(HttpRequestHeader.Authorization, string.Format("Basic {0}", authorization));
            request.Method = "POST";
            request.Accept = "application/json";
            request.ContentType = "application/json";
            request.ContentLength = postData.Length;
            try
            {
                using (var stream = request.GetRequestStream())
                {
                    stream.Write(postData, 0, postData.Length);
                }
                var httpResponse = (HttpWebResponse)request.GetResponse();
                using (var streamReader = new StreamReader(httpResponse.GetResponseStream()))
                {
                    return JsonConvert.DeserializeObject<PayPointPaymentResponse>(streamReader.ReadToEnd());
                }
            }
            catch (WebException ex)
            {
                var httpResponse = (HttpWebResponse)ex.Response;
                using (var streamReader = new StreamReader(httpResponse.GetResponseStream()))
                {
                    return JsonConvert.DeserializeObject<PayPointPaymentResponse>(streamReader.ReadToEnd());
                }
            }
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
            return new ProcessPaymentResult();
        }

        /// <summary>
        /// Post process payment (used by payment gateways that require redirecting to a third-party URL)
        /// </summary>
        /// <param name="postProcessPaymentRequest">Payment info required for an order processing</param>
        public void PostProcessPayment(PostProcessPaymentRequest postProcessPaymentRequest)
        {
            var storeLocation = _webHelper.GetStoreLocation();

            //create post data
            var payPointPayment = new PayPointPayment
            {
                Locale = _workContext.WorkingLanguage.UniqueSeoCode,
                Customer = new PayPointPaymentCustomer { Registered = false },
                Transaction = new PayPointPaymentTransaction
                {
                    MerchantReference = postProcessPaymentRequest.Order.OrderGuid.ToString(),
                    Money = new PayPointPaymentMoney
                    {
                        Currency = _currencyService.GetCurrencyById(_currencySettings.PrimaryStoreCurrencyId).CurrencyCode,
                        Amount = new PayPointPaymentAmount { Fixed = Math.Round(postProcessPaymentRequest.Order.OrderTotal, 2) }
                    },
                    Description = string.Format("Order #{0}", postProcessPaymentRequest.Order.Id)
                },
                Session = new PayPointPaymentSession
                {
                    ReturnUrl = new PayPointPaymentUrl { Url = string.Format("{0}checkout/completed/{1}", storeLocation, postProcessPaymentRequest.Order.Id) },
                    CancelUrl = new PayPointPaymentUrl { Url = string.Format("{0}orderdetails/{1}", storeLocation, postProcessPaymentRequest.Order.Id) },
                    TransactionNotification = new PayPointPaymentCallbackUrl
                    {
                        Format = PayPointPaymentFormat.REST_JSON,
                        Url = string.Format("{0}Plugins/PaymentPayPoint/Callback", storeLocation)
                    }
                }
            };

            //post request to API
            var payPointPaymentResponse = PostRequest(payPointPayment);

            //redirect to hosted payment service
            if (payPointPaymentResponse.Status == PayPointStatus.SUCCESS)
                _httpContext.Response.Redirect(payPointPaymentResponse.RedirectUrl);
            else
                _logger.Error(string.Format("PayPoint transaction failed. {0} - {1}", payPointPaymentResponse.ReasonCode, payPointPaymentResponse.ReasonMessage));

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
            var result = this.CalculateAdditionalFee(_orderTotalCalculationService, cart,
                _payPointPaymentSettings.AdditionalFee, _payPointPaymentSettings.AdditionalFeePercentage);

            return result;
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

        /// <summary>
        /// Get type of the controller
        /// </summary>
        /// <returns>Controller type</returns>
        public Type GetControllerType()
        {
            return typeof(PaymentPayPointController);
        }

        /// <summary>
        /// Install the plugin
        /// </summary>
        public override void Install()
        {
            //settings
            _settingService.SaveSetting(new PayPointPaymentSettings
            {
                UseSandbox = true
            });

            //locales
            this.AddOrUpdatePluginLocaleResource("Plugins.Payments.PayPoint.Fields.AdditionalFee", "Additional fee");
            this.AddOrUpdatePluginLocaleResource("Plugins.Payments.PayPoint.Fields.AdditionalFee.Hint", "Enter additional fee to charge your customers.");
            this.AddOrUpdatePluginLocaleResource("Plugins.Payments.PayPoint.Fields.AdditionalFeePercentage", "Additional fee. Use percentage");
            this.AddOrUpdatePluginLocaleResource("Plugins.Payments.PayPoint.Fields.AdditionalFeePercentage.Hint", "Determines whether to apply a percentage additional fee to the order total. If not enabled, a fixed value is used.");
            this.AddOrUpdatePluginLocaleResource("Plugins.Payments.PayPoint.Fields.ApiPassword", "API password");
            this.AddOrUpdatePluginLocaleResource("Plugins.Payments.PayPoint.Fields.ApiPassword.Hint", "Specify API password.");
            this.AddOrUpdatePluginLocaleResource("Plugins.Payments.PayPoint.Fields.ApiUsername", "API username");
            this.AddOrUpdatePluginLocaleResource("Plugins.Payments.PayPoint.Fields.ApiUsername.Hint", "Specify API username.");
            this.AddOrUpdatePluginLocaleResource("Plugins.Payments.PayPoint.Fields.InstallationId", "Installation ID");
            this.AddOrUpdatePluginLocaleResource("Plugins.Payments.PayPoint.Fields.InstallationId.Hint", "Specify installation ID.");
            this.AddOrUpdatePluginLocaleResource("Plugins.Payments.PayPoint.Fields.UseSandbox", "Use Sandbox");
            this.AddOrUpdatePluginLocaleResource("Plugins.Payments.PayPoint.Fields.UseSandbox.Hint", "Check to enable Sandbox (testing environment).");
            this.AddOrUpdatePluginLocaleResource("Plugins.Payments.PayPoint.RedirectionTip", "You will be redirected to PayPoint site to complete the order.");
            this.AddOrUpdatePluginLocaleResource("Plugins.Payments.PayPoint.PaymentMethodDescription", "You will be redirected to PayPoint site to complete the order.");
            
            base.Install();
        }

        /// <summary>
        /// Uninstall the plugin
        /// </summary>
        public override void Uninstall()
        {
            //settings
            _settingService.DeleteSetting<PayPointPaymentSettings>();

            //locales
            this.DeletePluginLocaleResource("Plugins.Payments.PayPoint.Fields.AdditionalFee");
            this.DeletePluginLocaleResource("Plugins.Payments.PayPoint.Fields.AdditionalFee.Hint");
            this.DeletePluginLocaleResource("Plugins.Payments.PayPoint.Fields.AdditionalFeePercentage");
            this.DeletePluginLocaleResource("Plugins.Payments.PayPoint.Fields.AdditionalFeePercentage.Hint");
            this.DeletePluginLocaleResource("Plugins.Payments.PayPoint.Fields.ApiPassword");
            this.DeletePluginLocaleResource("Plugins.Payments.PayPoint.Fields.ApiPassword.Hint");
            this.DeletePluginLocaleResource("Plugins.Payments.PayPoint.Fields.ApiUsername");
            this.DeletePluginLocaleResource("Plugins.Payments.PayPoint.Fields.ApiUsername.Hint");
            this.DeletePluginLocaleResource("Plugins.Payments.PayPoint.Fields.InstallationId");
            this.DeletePluginLocaleResource("Plugins.Payments.PayPoint.Fields.InstallationId.Hint");
            this.DeletePluginLocaleResource("Plugins.Payments.PayPoint.Fields.UseSandbox");
            this.DeletePluginLocaleResource("Plugins.Payments.PayPoint.Fields.UseSandbox.Hint");
            this.DeletePluginLocaleResource("Plugins.Payments.PayPoint.RedirectionTip");
            this.DeletePluginLocaleResource("Plugins.Payments.PayPoint.PaymentMethodDescription");

            base.Uninstall();
        }
        
        #endregion

        #region Properies

        /// <summary>
        /// Gets a value indicating whether capture is supported
        /// </summary>
        public bool SupportCapture
        {
            get { return false; }
        }

        /// <summary>
        /// Gets a value indicating whether partial refund is supported
        /// </summary>
        public bool SupportPartiallyRefund
        {
            get { return false; }
        }

        /// <summary>
        /// Gets a value indicating whether refund is supported
        /// </summary>
        public bool SupportRefund
        {
            get { return false; }
        }

        /// <summary>
        /// Gets a value indicating whether void is supported
        /// </summary>
        public bool SupportVoid
        {
            get { return false; }
        }

        /// <summary>
        /// Gets a recurring payment type of payment method
        /// </summary>
        public RecurringPaymentType RecurringPaymentType
        {
            get { return RecurringPaymentType.NotSupported; }
        }

        /// <summary>
        /// Gets a payment method type
        /// </summary>
        public PaymentMethodType PaymentMethodType
        {
            get { return PaymentMethodType.Redirection; }
        }

        /// <summary>
        /// Gets a value indicating whether we should display a payment information page for this plugin
        /// </summary>
        public bool SkipPaymentInfo
        {
            get { return false; }
        }

        /// <summary>
        /// Gets a payment method description that will be displayed on checkout pages in the public store
        /// </summary>
        public string PaymentMethodDescription
        {
            get { return _localizationService.GetResource("Plugins.Payments.PayPoint.PaymentMethodDescription"); }
        }

        #endregion
    }
}
