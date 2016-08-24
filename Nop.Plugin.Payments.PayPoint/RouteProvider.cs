using System.Web.Mvc;
using System.Web.Routing;
using Nop.Web.Framework.Mvc.Routes;

namespace Nop.Plugin.Payments.PayPoint
{
    public partial class RouteProvider : IRouteProvider
    {
        public void RegisterRoutes(RouteCollection routes)
        {
            routes.MapRoute("Plugin.Payments.PayPoint.Callback",
                 "Plugins/PaymentPayPoint/Callback",
                 new { controller = "PaymentPayPoint", action = "Callback" },
                 new[] { "Nop.Plugin.Payments.PayPoint.Controllers" }
            );
        }

        public int Priority
        {
            get { return 0; }
        }
    }
}
