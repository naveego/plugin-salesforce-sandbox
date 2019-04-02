using System;
using PluginSalesforce.Helper;
using Xunit;

namespace PluginSalesforceTest.Helper
{
    public class SettingsTest
    {
        [Fact]
        public void ValidateTest()
        {
            // setup
            var settings = new Settings {ClientId = "client", ClientSecret = "secret", RefreshToken = "refresh", InstanceUrl = "test"};
            
            // act
            settings.Validate();

            // assert
        }
        
        [Fact]
        public void ValidateNullClientIdTest()
        {
            // setup
            var settings = new Settings {ClientId = null, ClientSecret = "secret", RefreshToken = "refresh", InstanceUrl = "test"};
            
            // act
            Exception e  = Assert.Throws<Exception>(() => settings.Validate());

            // assert
            Assert.Contains("the ClientId property must be set", e.Message);
        }
        
        [Fact]
        public void ValidateNullClientSecretTest()
        {
            // setup
            var settings = new Settings {ClientId = "client", ClientSecret = null, RefreshToken = "refresh", InstanceUrl = "test"};
            
            // act
            Exception e  = Assert.Throws<Exception>(() => settings.Validate());

            // assert
            Assert.Contains("the ClientSecret property must be set", e.Message);
        }
        
        [Fact]
        public void ValidateNullRefreshTokenTest()
        {
            // setup
            var settings = new Settings {ClientId = "client", ClientSecret = "secret", RefreshToken = null, InstanceUrl = "test"};
            
            // act
            Exception e  = Assert.Throws<Exception>(() => settings.Validate());

            // assert
            Assert.Contains("the RefreshToken property must be set", e.Message);
        }
        
        [Fact]
        public void ValidateNullInstanceUrlTest()
        {
            // setup
            var settings = new Settings {ClientId = "client", ClientSecret = "secret", RefreshToken = "token", InstanceUrl = null};
            
            // act
            Exception e  = Assert.Throws<Exception>(() => settings.Validate());

            // assert
            Assert.Contains("the InstanceUrl property must be set", e.Message);
        }
    }
}