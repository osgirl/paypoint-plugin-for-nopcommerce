using System;
using System.IO;
using System.Net;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using Nop.Core;
using Nop.Plugin.Payments.PayPoint.Models;
using Nop.Services.Configuration;
using Nop.Services.Localization;
using Nop.Services.Logging;
using Nop.Services.Orders;
using Nop.Services.Stores;
using Nop.Web.Framework;
using Nop.Web.Framework.Controllers;
using Nop.Web.Framework.Mvc.Filters;

namespace Nop.Plugin.Payments.PayPoint.Controllers
{
    [AuthorizeAdmin]
    [Area(AreaNames.Admin)]
    public class PaymentPayPointController : BasePaymentController
    {
        #region Fields

        private readonly ILocalizationService _localizationService;
        private readonly ILogger _logger;
        private readonly IOrderProcessingService _orderProcessingService;
        private readonly IOrderService _orderService;
        private readonly ISettingService _settingService;
        private readonly IStoreService _storeService;
        private readonly IWorkContext _workContext;

        #endregion

        #region Ctor

        public PaymentPayPointController(ILocalizationService localizationService,
            ILogger logger,
            IOrderProcessingService orderProcessingService,
            IOrderService orderService,
            ISettingService settingService,
            IStoreService storeService,
            IWorkContext workContext)
        {
            this._localizationService = localizationService;
            this._logger = logger;
            this._orderProcessingService = orderProcessingService;
            this._orderService = orderService;
            this._settingService = settingService;
            this._storeService = storeService;
            this._workContext = workContext;
        }

        #endregion

        #region Methods
        
        public ActionResult Configure()
        {
            //load settings for a chosen store scope
            var storeScope = GetActiveStoreScopeConfiguration(_storeService, _workContext);
            var payPointPaymentSettings = _settingService.LoadSetting<PayPointPaymentSettings>(storeScope);

            var model = new ConfigurationModel
            {
                ApiUsername = payPointPaymentSettings.ApiUsername,
                ApiPassword = payPointPaymentSettings.ApiPassword,
                InstallationId = payPointPaymentSettings.InstallationId,
                UseSandbox = payPointPaymentSettings.UseSandbox,
                AdditionalFee = payPointPaymentSettings.AdditionalFee,
                AdditionalFeePercentage = payPointPaymentSettings.AdditionalFeePercentage,
                ActiveStoreScopeConfiguration = storeScope
            };

            if (storeScope > 0)
            {
                model.InstallationId_OverrideForStore = _settingService.SettingExists(payPointPaymentSettings, x => x.InstallationId, storeScope);
                model.UseSandbox_OverrideForStore = _settingService.SettingExists(payPointPaymentSettings, x => x.UseSandbox, storeScope);
                model.AdditionalFee_OverrideForStore = _settingService.SettingExists(payPointPaymentSettings, x => x.AdditionalFee, storeScope);
                model.AdditionalFeePercentage_OverrideForStore = _settingService.SettingExists(payPointPaymentSettings, x => x.AdditionalFeePercentage, storeScope);
            }

            return View("~/Plugins/Payments.PayPoint/Views/Configure.cshtml", model);
        }

        [HttpPost]
        public IActionResult Configure(ConfigurationModel model)
        {
            if (!ModelState.IsValid)
                return Configure();

            //load settings for a chosen store scope
            var storeScope = GetActiveStoreScopeConfiguration(_storeService, _workContext);
            var payPointPaymentSettings = _settingService.LoadSetting<PayPointPaymentSettings>(storeScope);

            //save settings
            payPointPaymentSettings.ApiUsername = model.ApiUsername;
            payPointPaymentSettings.ApiPassword = model.ApiPassword;
            payPointPaymentSettings.InstallationId = model.InstallationId;
            payPointPaymentSettings.UseSandbox = model.UseSandbox;
            payPointPaymentSettings.AdditionalFee = model.AdditionalFee;
            payPointPaymentSettings.AdditionalFeePercentage = model.AdditionalFeePercentage;

            /* We do not clear cache after each setting update.
             * This behavior can increase performance because cached settings will not be cleared 
             * and loaded from database after each update */
            _settingService.SaveSetting(payPointPaymentSettings, x => x.ApiUsername, storeScope, false);
            _settingService.SaveSetting(payPointPaymentSettings, x => x.ApiPassword, storeScope, false);
            _settingService.SaveSettingOverridablePerStore(payPointPaymentSettings, x => x.InstallationId, model.InstallationId_OverrideForStore, storeScope, false);
            _settingService.SaveSettingOverridablePerStore(payPointPaymentSettings, x => x.UseSandbox, model.UseSandbox_OverrideForStore, storeScope, false);
            _settingService.SaveSettingOverridablePerStore(payPointPaymentSettings, x => x.AdditionalFee, model.AdditionalFee_OverrideForStore, storeScope, false);
            _settingService.SaveSettingOverridablePerStore(payPointPaymentSettings, x => x.AdditionalFeePercentage, model.AdditionalFeePercentage_OverrideForStore, storeScope, false);

            //now clear settings cache
            _settingService.ClearCache();

            SuccessNotification(_localizationService.GetResource("Admin.Plugins.Saved"));

            return Configure();
        }
        
        public IActionResult Callback()
        {
            PayPointCallback payPointPaymentCallback = null;
            try
            {
                using (var streamReader = new StreamReader(HttpContext.Request.Body))
                {
                    payPointPaymentCallback = JsonConvert.DeserializeObject<PayPointCallback>(streamReader.ReadToEnd());
                }
            }
            catch (Exception ex)
            {
                _logger.Error("PayPoint callback error", ex);
                return new StatusCodeResult((int)HttpStatusCode.OK);
            }

            if (payPointPaymentCallback.Transaction.Status != PayPointStatus.SUCCESS)
            {
                _logger.Error($"PayPoint callback error. Transaction is {payPointPaymentCallback.Transaction.Status}");
                return new StatusCodeResult((int)HttpStatusCode.OK);
            }

            if (!Guid.TryParse(payPointPaymentCallback.Transaction.MerchantRef, out Guid orderGuid))
            {
                _logger.Error("PayPoint callback error. Data is not valid");
                return new StatusCodeResult((int)HttpStatusCode.OK);
            }

            var order = _orderService.GetOrderByGuid(orderGuid);
            if (order == null)
                return new StatusCodeResult((int)HttpStatusCode.OK);

            //paid order
            if (_orderProcessingService.CanMarkOrderAsPaid(order))
            {
                order.CaptureTransactionId = payPointPaymentCallback.Transaction.TransactionId;
                _orderService.UpdateOrder(order);
                _orderProcessingService.MarkOrderAsPaid(order);
            }

            return new StatusCodeResult((int)HttpStatusCode.OK);
        }

        #endregion
    }
}