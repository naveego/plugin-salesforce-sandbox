using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using PluginSalesforce.Helper;
using RichardSzalay.MockHttp;
using Xunit;

namespace PluginSalesforceTest.Helper
{
    public class AuthenticatorTest
    {
        [Fact]
        public async Task GetTokenTest()
        {
            // setup
            var mockHttp = new MockHttpMessageHandler();

            mockHttp.When("https://login.salesforce.com/services/oauth2/token")
                .Respond("application/json", "{\"access_token\":\"mocktoken\"}");

            var auth = new Authenticator(new Settings{ ClientId = "client", ClientSecret = "secret", RefreshToken = "refresh"}, mockHttp.ToHttpClient());
            
            // act
            var token = await auth.GetToken();
            var token2 = await auth.GetToken();

            // assert
            // first token is fetched
            Assert.Equal("mocktoken", token);
            // second token should be the same but not be fetched
            Assert.Equal("mocktoken", token2);
        }
        
        [Fact]
        public async Task GetTokenWithExceptionTest()
        {
            // setup
            var mockHttp = new MockHttpMessageHandler();

            mockHttp.When("https://login.salesforce.com/services/oauth2/token")
                .Respond(HttpStatusCode.Forbidden);

            var auth = new Authenticator(new Settings{ ClientId = "client", ClientSecret = "secret", RefreshToken = "refresh"}, mockHttp.ToHttpClient());
            
            // act
            Exception e  = await Assert.ThrowsAsync<HttpRequestException>(async () => await auth.GetToken());

            // assert
            Assert.Contains("403", e.Message);
        }
    }
}