using System.Collections.Generic;
using System.Text;
using Naveego.Sdk.Plugins;
using Newtonsoft.Json;

namespace PluginSalesforceSandbox.API.Read
{
    public static partial class Read
    {
        public static string GetSchemaJson()
        {
            var schemaJsonObj = new Dictionary<string, object>
            {
                {"type", "object"},
                {"properties", new Dictionary<string, object>
                {
                    {"BatchWindowSeconds", new Dictionary<string, object>
                    {
                        {"type", "number"},
                        {"title", "Batch Window"},
                        {"description", "Length of interval to wait between processing change notification events in a batch (default 5s)."},
                        {"default", 5},
                    }},
                    {"ChannelName", new Dictionary<string, object>
                    {
                        {"type", "string"},
                        {"title", "Channel Name"},
                        {"description", "Enter the name of the channel in Salesforce. To create or find an existing channel, please refer to this documentation: https://developer.salesforce.com/docs/atlas.en-us.change_data_capture.meta/change_data_capture/cdc_subscribe_channels.htm"}
                    }},
                    {"OrganizationId", new Dictionary<string, object>
                    {
                        {"type", "string"},
                        {"title", "Organization ID"},
                        {"description", "Enter the ID of the Organization in Salesforce. To find the Organiztion ID, please refer to this documentation: https://help.salesforce.com/s/articleView?id=000385215&type=1"}
                    }}
                }},
                {"required", new []
                {
                    "BatchWindowSeconds",
                    "ChannelName",
                    "OrganizationId"
                }}
            };

            return JsonConvert.SerializeObject(schemaJsonObj);
        }
    }
}