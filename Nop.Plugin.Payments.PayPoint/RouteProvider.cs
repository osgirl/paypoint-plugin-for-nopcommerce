using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Nop.Web.Framework.Mvc.Routing;

namespace Nop.Plugin.Payments.PayPoint
{
    public partial class RouteProvider : IRouteProvider
    {
        public void RegisterRoutes(IRouteBuilder routeBuilder)
        {
            routeBuilder.MapRoute("Plugin.Payments.PayPoint.Callback", "Plugins/PaymentPayPoint/Callback",
                new {controller = "PaymentPayPoint", action = "Callback"});
        }

        public int Priority
        {
            get { return 0; }
        }
    }
}
