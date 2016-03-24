using Microsoft.Owin;
using Owin;

[assembly: OwinStartupAttribute(typeof(MongoSessionStateStore.Test.Startup))]
namespace MongoSessionStateStore.Test
{
    public partial class Startup
    {
        public void Configuration(IAppBuilder app)
        {
            ConfigureAuth(app);
        }
    }
}
