using System.Threading.Tasks;
using PluginSalesforceSandbox.API.Utility;
using PluginSalesforceSandbox.Helper;
using PluginSalesforceSandbox.Helper;

namespace PluginSalesforceSandbox.API.Factory
{
    public interface IPushTopicConnectionFactory
    {
        PushTopicConnection GetPushTopicConnection(RequestHelper requestHelper, string channel);
    }
}