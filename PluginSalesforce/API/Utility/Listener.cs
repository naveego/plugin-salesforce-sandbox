using CometD.NetCore.Bayeux;
using CometD.NetCore.Bayeux.Client;
using System;
using System.Collections.Generic;

namespace PluginSalesforce.API.Utility
{
    public class Listener : IMessageListener
    {

        public List<string> messages = new List<string>();
        public void OnMessage(IClientSessionChannel channel, IMessage message)
        {
            var convertedJson = message.Json;
            messages.Add(convertedJson);
            Console.WriteLine(convertedJson);
        }

        public void ClearStoredMessages()
        {
            messages.Clear();
        }

        public List<string> GetMessages()
        {
            return messages;
        }
    }
}