using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using Microsoft.AspNetCore.Http;
using Newtonsoft.Json;
using Nop.Core;
using Nop.Core.Domain.Directory;
using Nop.Core.Domain.Orders;
using Nop.Core.Domain.Payments;
using Nop.Core.Plugins;
using Nop.Services.Configuration;
using Nop.Services.Directory;
using Nop.Services.Localization;
using Nop.Services.Logging;
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
        private readonly ICurrencyService _currencyService;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly ILocalizationService _localizationService;
        private readonly ILogger _logger;
        private readonly IPaymentService _paymentService;
        private readonly ISettingService _settingService;
        private readonly IWebHelper _webHelper;
        private readonly IWorkContext _workContext;
        private readonly PayPointPaymentSettings _payPointPaymentSettings;

        #endregion

        #region Ctor

        public PayPointPaymentProcessor(CurrencySettings currencySettings,
            ICurrencyService currencyService,
            IHttpContextAccessor httpContextAccessor,
            ILocalizationService localizationService,
            ILogger logger,
            IPaymentService paymentService,
            ISettingService settingService,
            IWebHelper webHelper,
            IWorkContext workContext,
            PayPointPaymentSettings payPointPaymentSettings)
        {
            this._currencySettings = currencySettings;
            this._currencyService = currencyService;
            this._httpContextAccessor = httpContextAccessor;
            this._localizationService = localizationService;
            this._logger = logger;
            this._paymentService = paymentService;
            this._settingService = settingService;
            this._webHelper = webHelper;
            this._workContext = workContext;
            this._payPointPaymentSettings = payPointPaymentSettings;
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
            var login = $"{_payPointPaymentSettings.ApiUsername}:{_payPointPaymentSettings.ApiPassword}";
            var authorization = Convert.ToBase64String(Encoding.Default.GetBytes(login));
            var request = (HttpWebRequest)WebRequest.Create($"{serviceUrl}/hosted/rest/sessions/{_payPointPaymentSettings.InstallationId}/payments");
            request.Headers.Add(HttpRequestHeader.Authorization, $"Basic {authorization}");
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
                    var dd = JsonConvert.DeserializeObject<PayPointPaymentResponse>(streamReader.ReadToEnd());
                    return dd;
                    //return JsonConvert.DeserializeObject<PayPointPaymentResponse>(streamReader.ReadToEnd());
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
                    ReturnUrl = new PayPointPaymentUrl { Url = $"{storeLocation}checkout/completed/{postProcessPaymentRequest.Order.Id}"
                    },
                    CancelUrl = new PayPointPaymentUrl { Url = $"{storeLocation}orderdetails/{postProcessPaymentRequest.Order.Id}"
                    },
                    TransactionNotification = new PayPointPaymentCallbackUrl
                    {
                        Format = PayPointPaymentFormat.REST_JSON,
                        Url = $"{storeLocation}Plugins/PaymentPayPoint/Callback"
                    }
                }
            };
                 
            //post request to API
            var payPointPaymentResponse = PostRequest(payPointPayment);

            //redirect to hosted payment service
            if (payPointPaymentResponse.Status == PayPointStatus.SUCCESS)
                _httpContextAccessor.HttpContext.Response.Redirect(payPointPaymentResponse.RedirectUrl);
            else
                _logger.Error($"PayPoint transaction failed. {payPointPaymentResponse.ReasonCode} - {payPointPaymentResponse.ReasonMessage}");

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
            var result = _paymentService.CalculateAdditionalFee(cart,
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
                throw new ArgumentNullException(nameof(order));

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
        
        public IList<string> ValidatePaymentForm(IFormCollection form)
        {
            return new List<string>();
        }

        public ProcessPaymentRequest GetPaymentInfo(IFormCollection form)
        {
            return new ProcessPaymentRequest();
        }

        public string GetPublicViewComponentName()
        {
            return "PaymentPayPoint";
        }

        public override string GetConfigurationPageUrl()
        {
            return $"{_webHelper.GetStoreLocation()}Admin/PaymentPayPoint/Configure";
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
            _localizationService.AddOrUpdatePluginLocaleResource("Plugins.Payments.PayPoint.Fields.AdditionalFee", "Additional fee");
            _localizationService.AddOrUpdatePluginLocaleResource("Plugins.Payments.PayPoint.Fields.AdditionalFee.Hint", "Enter additional fee to charge your customers.");
            _localizationService.AddOrUpdatePluginLocaleResource("Plugins.Payments.PayPoint.Fields.AdditionalFeePercentage", "Additional fee. Use percentage");
            _localizationService.AddOrUpdatePluginLocaleResource("Plugins.Payments.PayPoint.Fields.AdditionalFeePercentage.Hint", "Determines whether to apply a percentage additional fee to the order total. If not enabled, a fixed value is used.");
            _localizationService.AddOrUpdatePluginLocaleResource("Plugins.Payments.PayPoint.Fields.ApiPassword", "API password");
            _localizationService.AddOrUpdatePluginLocaleResource("Plugins.Payments.PayPoint.Fields.ApiPassword.Hint", "Specify API password.");
            _localizationService.AddOrUpdatePluginLocaleResource("Plugins.Payments.PayPoint.Fields.ApiUsername", "API username");
            _localizationService.AddOrUpdatePluginLocaleResource("Plugins.Payments.PayPoint.Fields.ApiUsername.Hint", "Specify API username.");
            _localizationService.AddOrUpdatePluginLocaleResource("Plugins.Payments.PayPoint.Fields.InstallationId", "Installation ID");
            _localizationService.AddOrUpdatePluginLocaleResource("Plugins.Payments.PayPoint.Fields.InstallationId.Hint", "Specify installation ID.");
            _localizationService.AddOrUpdatePluginLocaleResource("Plugins.Payments.PayPoint.Fields.UseSandbox", "Use Sandbox");
            _localizationService.AddOrUpdatePluginLocaleResource("Plugins.Payments.PayPoint.Fields.UseSandbox.Hint", "Check to enable Sandbox (testing environment).");
            _localizationService.AddOrUpdatePluginLocaleResource("Plugins.Payments.PayPoint.RedirectionTip", "You will be redirected to PayPoint site to complete the order.");
            _localizationService.AddOrUpdatePluginLocaleResource("Plugins.Payments.PayPoint.PaymentMethodDescription", "You will be redirected to PayPoint site to complete the order.");
            
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
            _localizationService.DeletePluginLocaleResource("Plugins.Payments.PayPoint.Fields.AdditionalFee");
            _localizationService.DeletePluginLocaleResource("Plugins.Payments.PayPoint.Fields.AdditionalFee.Hint");
            _localizationService.DeletePluginLocaleResource("Plugins.Payments.PayPoint.Fields.AdditionalFeePercentage");
            _localizationService.DeletePluginLocaleResource("Plugins.Payments.PayPoint.Fields.AdditionalFeePercentage.Hint");
            _localizationService.DeletePluginLocaleResource("Plugins.Payments.PayPoint.Fields.ApiPassword");
            _localizationService.DeletePluginLocaleResource("Plugins.Payments.PayPoint.Fields.ApiPassword.Hint");
            _localizationService.DeletePluginLocaleResource("Plugins.Payments.PayPoint.Fields.ApiUsername");
            _localizationService.DeletePluginLocaleResource("Plugins.Payments.PayPoint.Fields.ApiUsername.Hint");
            _localizationService.DeletePluginLocaleResource("Plugins.Payments.PayPoint.Fields.InstallationId");
            _localizationService.DeletePluginLocaleResource("Plugins.Payments.PayPoint.Fields.InstallationId.Hint");
            _localizationService.DeletePluginLocaleResource("Plugins.Payments.PayPoint.Fields.UseSandbox");
            _localizationService.DeletePluginLocaleResource("Plugins.Payments.PayPoint.Fields.UseSandbox.Hint");
            _localizationService.DeletePluginLocaleResource("Plugins.Payments.PayPoint.RedirectionTip");
            _localizationService.DeletePluginLocaleResource("Plugins.Payments.PayPoint.PaymentMethodDescription");

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
