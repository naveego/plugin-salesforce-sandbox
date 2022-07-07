using System.Collections.Generic;

namespace PluginSalesforce.API.Read
{
    public class RealTimeSettings
    {
        public string ChannelName { get; set; }
        public int BatchWindow { get; set; } = 5;
    }
}