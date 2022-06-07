using System.Collections.Generic;

namespace PluginSalesforce.API.Read
{
    public class RealTimeSettings
    {
        // public int PollingIntervalSeconds { get; set; } = 5;
        public List<SObjectInfo> SObjectInformation { get; set; } = new List<SObjectInfo>();

        public class SObjectInfo
        {
            public string Id { get; set; }
            public List<string> PrimaryKeyList { get; set; }
        };
    }
}