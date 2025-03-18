using PluginSalesforceSandbox.Helper;

namespace PluginSalesforceSandbox.API.Factory
{
    public interface ISalesforcePubSubClientFactory
    {
        SalesforcePubSubClient GetPubSubClient(string accessToken, string instanceUrl, string organizationId);
    }
}