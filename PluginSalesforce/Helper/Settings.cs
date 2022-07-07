using System;

namespace PluginSalesforce.Helper
{
    public class Settings
    {
        public string ClientId { get; set; }
        public string ClientSecret { get; set; }
        public string RefreshToken { get; set; }
        public string InstanceUrl { get; set; }

        /// <summary>
        /// Validates the settings input object
        /// </summary>
        /// <exception cref="Exception"></exception>
        public void Validate()
        {
            if (string.IsNullOrEmpty(ClientId))
            {
                throw new Exception("the ClientId property must be set");
            }
            
            if (string.IsNullOrEmpty(ClientSecret))
            {
                throw new Exception("the ClientSecret property must be set");
            }
            
            if (string.IsNullOrEmpty(RefreshToken))
            {
                throw new Exception("the RefreshToken property must be set");
            }
            
            if (string.IsNullOrEmpty(InstanceUrl))
            {
                throw new Exception("the InstanceUrl property must be set");
            }
        }
    }
}