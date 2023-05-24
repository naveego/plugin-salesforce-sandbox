using System;
using System.Collections.Generic;
using CometD.NetCore.Client;
using Naveego.Sdk.Logging;
using Naveego.Sdk.Plugins;
using PluginSalesforce.API.Utility;

namespace PluginSalesforce.API.Factory
{
    public class PushTopicConnection
    {
        private readonly BayeuxClient _bayeuxClient = null;
        private readonly Listener _listener = null;
        private readonly string _channel = "";

        public PushTopicConnection(BayeuxClient bayeuxClient, string channel)
        {
            _bayeuxClient = bayeuxClient;
            _channel = channel;
            _listener = new Listener();
        }
        public void Connect()
        {
            _bayeuxClient.Handshake();
            _bayeuxClient.WaitFor(1000, new[] { BayeuxClient.State.CONNECTED });
            _bayeuxClient.GetChannel(_channel).Subscribe(_listener);
            Logger.Info($"Waiting event from salesforce for the push topic {_channel}");
        }
        public void Disconnect()
        {
            _bayeuxClient.Disconnect();
            _bayeuxClient.WaitFor(1000, new[] { BayeuxClient.State.DISCONNECTED });
        }

        public async IAsyncEnumerable<string> GetCurrentMessages()
        {
            if (!_bayeuxClient.Connected) {
                Connect();
            }

            var messages = _listener.GetMessages();
            
            foreach (var message in messages)
            {
                yield return message;
            }
        }

        public void ClearStoredMessages()
        {
            _listener.ClearStoredMessages();
        }

        public bool HasMessages()
        {
            return _listener.GetMessages().Count > 0;
        }
    }
}