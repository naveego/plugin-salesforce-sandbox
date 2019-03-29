using Newtonsoft.Json;

namespace PluginSalesforce.DataContracts
{
    public class FieldObject
    {
        [JsonProperty("autoNumber")]
        public bool AutoNumber { get; set; }
        
        [JsonProperty("defaultedOnCreate")]
        public bool DefaultedOnCreate { get; set; }
        
        [JsonProperty("idLookup")]
        public bool IdLookup { get; set; }
        
        [JsonProperty("label")]
        public string Label { get; set; }
        
        [JsonProperty("length")]
        public int Length { get; set; }
        
        [JsonProperty("name")]
        public string Name { get; set; }
        
        [JsonProperty("precision")]
        public int Precision { get; set; }
        
        [JsonProperty("soapType")]
        public string SoapType { get; set; }
        
        [JsonProperty("type")]
        public string Type { get; set; }
        
        [JsonProperty("unique")]
        public bool Unique { get; set; }
    }
}