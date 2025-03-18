using System;
using Grpc.Core;
using PluginSalesforceSandbox.Helper;

namespace PluginSalesforceSandbox.API.Factory
{
    public class SalesforcePubSubClientFactory : ISalesforcePubSubClientFactory
    {
        public SalesforcePubSubClient GetPubSubClient(string accessToken, string instanceUrl, string organizationId)
        {
            SalesforcePubSubClient salesforcePubSubClient = null;

            try
            {
                var metadata = new Metadata{
                    {"accesstoken", accessToken},
                    { "instanceurl", instanceUrl},
                    { "tenantid", organizationId}
                };

                salesforcePubSubClient = new SalesforcePubSubClient("https://api.pubsub.salesforce.com:7443", metadata);
            }
            catch (Exception e)
            {
                throw new Exception("Error creating pub sub client: " + e.Message);
            }

            if (salesforcePubSubClient == null)
            {
                throw new Exception("Error creating pub sub client");
            }

            return salesforcePubSubClient;
        }
    }
}