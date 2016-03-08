using System.Web.Mvc;
using System.Web.Routing;
using Nop.Web.Framework.Mvc.Routes;

namespace Nop.Plugin.Payments.PayPoint
{
    public partial class RouteProvider : IRouteProvider
    {
        public void RegisterRoutes(RouteCollection routes)
        {
            //Return
            routes.MapRoute("Plugin.Payments.PayPoint.Return",
                 "Plugins/PaymentPayPoint/Return",
                 new { controller = "PaymentPayPoint", action = "Return" },
                 new[] { "Nop.Plugin.Payments.PayPoint.Controllers" }
            );
        }
        public int Priority
        {
            get
            {
                return 0;
            }
        }
    }
}
