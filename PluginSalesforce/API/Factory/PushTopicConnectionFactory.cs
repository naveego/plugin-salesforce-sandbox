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
                // var authResponse = Task.Run(() => LoginController.AsyncAuthRequest());
                // authResponse.Wait();
                
                ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
                int readTimeOut = 120000;
                string streamingEndpointURI = "/cometd/52.0";
                var options = new Dictionary<string, object>
                {
                    {ClientTransport.TIMEOUT_OPTION, readTimeOut}
                };
                NameValueCollection collection = new NameValueCollection();
                // collection.Add(HttpRequestHeader.Authorization.ToString(), "OAuth " + authResponse.Result.access_token);
                collection.Add(HttpRequestHeader.Authorization.ToString(), "Bearer " + accessToken);
                var transport = new LongPollingTransport(options, new NameValueCollection {collection});
                // var serverUri = new Uri(authResponse.Result.instance_url);
                var serverUri = new Uri(instanceUrl);
                String endpoint = String.Format("{0}://{1}{2}", serverUri.Scheme, serverUri.Host, streamingEndpointURI);
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