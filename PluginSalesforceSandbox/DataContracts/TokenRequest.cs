using Newtonsoft.Json;

namespace PluginSalesforceSandbox.DataContracts
{
    public class TokenRequest
    {
        [JsonProperty("code")]
        public string Code { get; set; }
        
        [JsonProperty("client_id")]
        public string ClientId { get; set; }
        
        [JsonProperty("client_secret")]
        public string ClientSecret { get; set; }
        
        [JsonProperty("grant_type")]
        public string GrantType { get; set; }
        
        [JsonProperty("redirect_uri")]
        public string RedirectUri { get; set; }
        
        [JsonProperty("refresh_token")]
        public string RefreshToken { get; set; }
    }
}