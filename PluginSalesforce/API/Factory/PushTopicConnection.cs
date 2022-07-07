using System;
using System.Collections.Generic;
using CometD.NetCore.Client;
using Naveego.Sdk.Plugins;
using PluginSalesforce.API.Utility;

namespace PluginSalesforce.API.Factory
{
    public class PushTopicConnection
    {
        private BayeuxClient BayeuxClient = null;
        private Listener Listener = null;
        private string Channel = "";

        private bool IsClearing = false;
        
        public PushTopicConnection(BayeuxClient bayeuxClient, string channel)
        {
            BayeuxClient = bayeuxClient;
            Channel = channel;
            Listener = new Listener();
        }
        public void Connect()
        {
            BayeuxClient.Handshake();
            BayeuxClient.WaitFor(1000, new[] { BayeuxClient.State.CONNECTED });
            BayeuxClient.GetChannel(Channel).Subscribe(Listener);
            Console.WriteLine("[INFO] Waiting event from salesforce for the push topic " + Channel.ToString());
        }
        public void Disconnect()
        {
            BayeuxClient.Disconnect();
            BayeuxClient.WaitFor(1000, new[] { BayeuxClient.State.DISCONNECTED });
        }

        public async IAsyncEnumerable<string> GetCurrentMessages()
        {
            var messages = Listener.GetMessages();
            
            foreach (var message in messages)
            {
                yield return message;
            }
        }

        public void ClearStoredMessages()
        {
            Listener.ClearStoredMessages();
        }

        public bool HasMessages()
        {
            return Listener.GetMessages().Count > 0;
        }
    }
}