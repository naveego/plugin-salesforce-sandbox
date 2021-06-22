using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using PluginSalesforceSandbox.Helper;
using RichardSzalay.MockHttp;
using Xunit;

namespace PluginSalesforceSandboxTest.Helper
{
    public class RequestHelperTest
    {
        [Fact]
        public async Task GetAsyncTest()
        {
            // setup
            var mockHttp = new MockHttpMessageHandler();
            
            mockHttp.When("https://test.salesforce.com/services/oauth2/token")
                .Respond("application/json", "{\"access_token\":\"mocktoken\"}");
            
            mockHttp.When("https://test.salesforce.com/services/data/v52.0/test")
                .Respond("application/json", "success");

            var requestHelper = new RequestHelper(new Settings{ ClientId = "client", ClientSecret = "secret", RefreshToken = "refresh", InstanceUrl = "https://test.salesforce.com"}, mockHttp.ToHttpClient());
            
            // act
            var response = await requestHelper.GetAsync("/test");

            // assert
            Assert.Equal("success", await response.Content.ReadAsStringAsync());
        }
        
        [Fact]
        public async Task GetAsyncWithTokenExceptionTest()
        {
            // setup
            var mockHttp = new MockHttpMessageHandler();
            
            mockHttp.When("https://test.salesforce.com/services/oauth2/token")
                .Respond(HttpStatusCode.Forbidden);

            mockHttp.When("https://test.salesforce.com/services/data/v52.0/test")
                .Respond(HttpStatusCode.Unauthorized);

            var requestHelper = new RequestHelper(new Settings{ ClientId = "client", ClientSecret = "secret", RefreshToken = "refresh", InstanceUrl = "https://test.salesforce.com"}, mockHttp.ToHttpClient());

            // act
            Exception e  = await Assert.ThrowsAsync<HttpRequestException>(async () => await requestHelper.GetAsync("/test"));

            // assert
            Assert.Contains("403", e.Message);
        }
        
        [Fact]
        public async Task GetAsyncWithRequestExceptionTest()
        {
            // setup
            var mockHttp = new MockHttpMessageHandler();
            
            mockHttp.When("https://test.salesforce.com/services/oauth2/token")
                .Respond("application/json", "{\"access_token\":\"mocktoken\"}");
            
            mockHttp.When("https://test.salesforce.com/services/data/v52.0/test")
                .Throw(new Exception("bad stuff"));

            var requestHelper = new RequestHelper(new Settings{ ClientId = "client", ClientSecret = "secret", RefreshToken = "refresh",  InstanceUrl = "https://test.salesforce.com"}, mockHttp.ToHttpClient());
            
            // act
            Exception e  = await Assert.ThrowsAsync<Exception>(async () => await requestHelper.GetAsync("/test"));

            // assert
            Assert.Contains("bad stuff", e.Message);
        }
    }
}