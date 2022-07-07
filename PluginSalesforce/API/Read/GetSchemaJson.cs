using System.Collections.Generic;
using Newtonsoft.Json;

namespace PluginSalesforce.API.Read
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
                    {"BatchWindow", new Dictionary<string, object>
                    {
                        {"type", "number"},
                        {"title", "Batch Window"},
                        {"description", "Length of interval to wait between processing real time jobs in seconds (default 5s)."},
                        {"default", 5},
                    }},
                    {"ChannelName", new Dictionary<string, object>
                    {
                        {"type", "string"},
                        {"title", "Channel Name"},
                        {"description", "Enter the name of the channel in Salesforce. To create or find an existing channel, please refer to this documentation: https://docs.naveego.com/docs/plugins/plugin-salesforce-realtime.html"}
                    }}
                }},
                {"required", new []
                {
                    "BatchWindow",
                    "ChannelName"
                }}
            };
            
            return JsonConvert.SerializeObject(schemaJsonObj);
        }
    }
}