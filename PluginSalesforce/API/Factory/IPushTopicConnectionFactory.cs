using System.Threading.Tasks;
using PluginSalesforce.API.Utility;
using PluginSalesforce.Helper;

namespace PluginSalesforce.API.Factory
{
    public interface IPushTopicConnectionFactory
    {
        PushTopicConnection GetPushTopicConnection(RequestHelper requestHelper, string channel);
    }
}