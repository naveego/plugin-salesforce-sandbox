using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace PluginSalesforce.API.Utility
{
    public class LoginController
    {
        public async static Task<AuthResponse> AsyncAuthRequest()
        {
            string USERNAME = "chris.cowell@aunalytics.com";
            string PASSWORD = "Smallestzebra97!";
            string TOKEN = "prfnRBJ5adFnbBReplraIlyI";
            string CONSUMER_KEY = "3MVG9FMtW0XJDLd2pdC4I7Pg0pRKQCZfJKRBZ4LyPORaSByg4RnoUuIiBvaLeHXSwZ1.K5.GV659GWgLXAOD1";
            string CONSUMER_SECRET = "B65B138D5554DA639F636D389375E8ED3C935344050CCCC15385D6BC9B702451";
            string TOKEN_REQUEST_ENDPOINTURL = "https://login.salesforce.com/services/oauth2/token";
            string TOKEN_REQUEST_QUERYURL = "/services/data/v43.0/query?q=select+Id+,name+from+account+limit+10";
            
            var content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("grant_type", "password"),
                new KeyValuePair<string, string>("client_id", CONSUMER_KEY),
                new KeyValuePair<string, string>("client_secret", CONSUMER_SECRET),
                new KeyValuePair<string, string>("username", USERNAME),
                new KeyValuePair<string, string>("password", PASSWORD + TOKEN)
            });
            HttpClient _httpClient = new HttpClient();
            var request = new HttpRequestMessage
            {
                Method = HttpMethod.Post,
                RequestUri = new Uri(TOKEN_REQUEST_ENDPOINTURL),
                Content = content
            };
            var responseMessage = await _httpClient.SendAsync(request).ConfigureAwait(false);
            var response = await responseMessage.Content.ReadAsStringAsync().ConfigureAwait(false);
            AuthResponse responseDyn = JsonConvert.DeserializeObject<AuthResponse>(response);
            return responseDyn;
        }
    }
}