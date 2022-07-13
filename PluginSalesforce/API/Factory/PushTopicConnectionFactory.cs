using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Net;
using System.Threading.Tasks;
using CometD.NetCore.Client;
using CometD.NetCore.Client.Transport;
using PluginSalesforce.API.Utility;
using PluginSalesforce.Helper;

namespace PluginSalesforce.API.Factory
{
    public class PushTopicConnectionFactory : IPushTopicConnectionFactory
    {
        public PushTopicConnection GetPushTopicConnection(RequestHelper requestHelper, string channel)
        {
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
            PushTopicConnection pushTopicConnection = null;

            var accessToken = requestHelper.GetToken();
            var instanceUrl = requestHelper.GetInstanceUrl();
            
            try
            {
                ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
                var readTimeOut = 120000;
                var streamingEndpointURI = "/cometd/52.0";
                var options = new Dictionary<string, object>
                {
                    {ClientTransport.TIMEOUT_OPTION, readTimeOut}
                };
                var collection = new NameValueCollection
                {
                    {HttpRequestHeader.Authorization.ToString(), "Bearer " + accessToken}
                };
                var transport = new LongPollingTransport(options, new NameValueCollection {collection});
                var serverUri = new Uri(instanceUrl);
                var endpoint = $"{serverUri.Scheme}://{serverUri.Host}{streamingEndpointURI}";
                var bayeuxClient = new BayeuxClient(endpoint, new[] {transport});

                pushTopicConnection = new PushTopicConnection(bayeuxClient, channel);
            }
            catch (Exception e)
            {
                throw new Exception("Error creating push topic connection: " + e.Message);
            }

            if (pushTopicConnection == null)
            {
                throw new Exception("Error creating push topic connection");
            }
            
            return pushTopicConnection;
        }
    }
}