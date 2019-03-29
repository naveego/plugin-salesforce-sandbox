namespace PluginSalesforce.DataContracts
{
    public class OAuthState
    {
        public string AuthToken     { get; set; }
        public string RefreshToken  { get; set; }
        public string Config { get; set; }
    }
}