using Newtonsoft.Json;

namespace PluginSalesforce.DataContracts
{
    public class TabObject
    {
        [JsonProperty("custom")]
        public bool Custom { get; set; }
        
        [JsonProperty("label")]
        public string Label { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }
        
        [JsonProperty("sobjectName")]
        public string SobjectName { get; set; }
    }
}