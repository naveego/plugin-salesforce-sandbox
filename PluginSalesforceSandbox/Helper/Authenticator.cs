using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Naveego.Sdk.Logging;
using Newtonsoft.Json;
using PluginSalesforceSandbox.DataContracts;

namespace PluginSalesforceSandbox.Helper
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
                    var requestUri = "https://test.salesforce.com/services/oauth2/token";

                    var formData = new List<KeyValuePair<string, string>>
                    {
                        new KeyValuePair<string, string>("grant_type", "refresh_token"),
                        new KeyValuePair<string, string>("client_id", _settings.ClientId),
                        new KeyValuePair<string, string>("client_secret", _settings.ClientSecret),
                        new KeyValuePair<string, string>("refresh_token", _settings.RefreshToken)
                    };

                    var body = new FormUrlEncodedContent(formData);
                    
                    var client = _client;
                    client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            
                    var response = await _client.PostAsync(requestUri, body);
                    response.EnsureSuccessStatusCode();
                    
                    var content = JsonConvert.DeserializeObject<TokenResponse>(await response.Content.ReadAsStringAsync());
                    
                    // update expiration and saved token
                    _expires = DateTime.Now.AddSeconds(3600);
                    _token = content.AccessToken;

                    return _token;
                }
                catch (Exception e)
                {
                    Logger.Error(e, e.Message);
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