﻿using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Net;
using CometD.NetCore.Client;
using CometD.NetCore.Client.Transport;
using PluginSalesforceSandbox.Helper;

namespace PluginSalesforceSandbox.API.Factory
{
    public class PushTopicConnectionFactory : IPushTopicConnectionFactory
    {
        public PushTopicConnection GetPushTopicConnection(RequestHelper requestHelper, string channel)
        {
            PushTopicConnection pushTopicConnection = null;

            var accessToken = requestHelper.GetToken();
            var instanceUrl = requestHelper.GetInstanceUrl();
            
            try
            {
                switch (requestHelper.GetTlsVersion())
                {
                    case "TLS 1.2":
                        ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
                        break;
                    case "TLS 1.3":
                        ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls13;
                        break;
                    default:
                        ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
                        break;
                }

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
                var transport = new LongPollingTransport(options, new NameValueCollection { collection });
                var serverUri = new Uri(instanceUrl);
                var endpoint = $"{serverUri.Scheme}://{serverUri.Host}{streamingEndpointURI}";
                var bayeuxClient = new BayeuxClient(endpoint, new[] { transport });

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