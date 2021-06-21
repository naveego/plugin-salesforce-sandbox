using System.Collections.Generic;
using Newtonsoft.Json;

namespace PluginSalesforceSandbox.DataContracts
{
    public class DescribeResponse
    {
        [JsonProperty("custom")]
        public bool Custom { get; set; }
        
        [JsonProperty("fields")]
        public List<FieldObject> Fields { get; set; }
        
        [JsonProperty("label")]
        public string Label { get; set; }
        
        [JsonProperty("labelPlural")]
        public string LabelPlural { get; set; }
        
        [JsonProperty("name")]
        public string Name { get; set; }
        
        [JsonProperty("queryable")]
        public bool Queryable { get; set; }
    }
}