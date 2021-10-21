using System.Collections.Generic;
using Newtonsoft.Json;

namespace PluginSalesforceSandbox.DataContracts
{
    public class RecordsResponse
    {
        [JsonProperty("done")]
        public bool Done { get; set; }
        
        [JsonProperty("nextRecordsUrl")]
        public string NextRecordsUrl { get; set; }
        
        [JsonProperty("records")]
        public List<Dictionary<string,object>> Records { get; set; }
        
        [JsonProperty("totalSize")]
        public int TotalSize { get; set; }
    }
}