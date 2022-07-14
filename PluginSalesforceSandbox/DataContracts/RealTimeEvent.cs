using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace PluginSalesforceSandbox.DataContracts
{
    public class RealTimeEventWrapper
    {
        [JsonProperty("data")]
        public RealTimeData Data { get; set; }
    }

    public class RealTimeData
    {
        [JsonProperty("event")]
        public RealTimeEvent Event { get; set; }
        
        [JsonProperty("sobject")]
        public Dictionary<string, object> SObject { get; set; }
        
        [JsonProperty("channel")]
        public string Channel { get; set; }
    }

    public class RealTimeEvent
    {
        [JsonProperty("createdDate")]
        public DateTime CreatedDate { get; set; }
        
        [JsonProperty("replayId")]
        public Int32 ReplayId { get; set; }
        
        [JsonProperty("type")]
        public string Type { get; set; }
    }
}