using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Newtonsoft.Json;
using PluginSalesforce.DataContracts;

namespace PluginSalesforce.Helper
{
    public class Authenticator
    {
        private readonly HttpClient _client;
        private readonly Settings _settings;
        private DateTime _expires;
        private string _token;

        public Authenticator(Settings settings, HttpClient client)
        {
            _client = client;
            _expires = DateTime.Now;
            _settings = settings;
            _token = String.Empty;
        }

        /// <summary>
        /// Get a token for the Salesforce API
        /// </summary>
        /// <returns></returns>
        public async Task<string> GetToken()
        {
            // check if token is expired or will expire in 5 minutes or less
            if (DateTime.Compare(DateTime.Now.AddMinutes(5), _expires) >= 0)
            {
                try
                {
                    // get a token
                    var requestUri = "https://login.salesforce.com/services/oauth2/token";
                    var json = new StringContent(JsonConvert.SerializeObject(new TokenRequest
                    {
                        ClientId = _settings.ClientId,
                        ClientSecret = _settings.ClientSecret,
                        GrantType = "refresh_token",
                        RefreshToken = _settings.RefreshToken
                    }));
                    
                    var client = _client;
                    client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            
                    var response = await _client.PostAsync(requestUri, json);
                    response.EnsureSuccessStatusCode();

                    var content = JsonConvert.DeserializeObject<TokenResponse>(await response.Content.ReadAsStringAsync());
                    
                    // update expiration and saved token
                    _expires = DateTime.Now.AddSeconds(3600);
                    _token = content.AccessToken;

                    return _token;
                }
                catch (Exception e)
                {
                    Logger.Error(e.Message);
                    throw;
                }
            }
            // return saved token
            else
            {
                return _token;
            }
        }
    }
}