using System.Collections.Generic;
using Newtonsoft.Json;

namespace PluginSalesforce.API.Read
{
    public static partial class Read
    {
        public static string GetUIJson()
        {
            var uiJsonObj = new Dictionary<string, object>
            {
                {
                    "ui:order", new[]
                    {
                        "ChannelName",
                        "BatchWindow"
                    }
                }
            };

            return JsonConvert.SerializeObject(uiJsonObj);
        }
    }
}