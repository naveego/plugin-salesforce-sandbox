using System.Collections.Generic;

namespace PluginSalesforce.API.Read
{
    public class RealTimeSettings
    {
        public string ChannelName { get; set; }
        public int BatchWindowSeconds { get; set; } = 5;
    }
}