using CometD.NetCore.Bayeux;
using CometD.NetCore.Bayeux.Client;
using System;
using System.Collections.Generic;
using Naveego.Sdk.Logging;

namespace PluginSalesforceSandbox.API.Utility
{
    public class Listener : IMessageListener
    {
        private readonly List<string> _messages = new List<string>();
        public void OnMessage(IClientSessionChannel channel, IMessage message)
        {
            var convertedJson = message.Json;
            _messages.Add(convertedJson);
            Logger.Info($"Got message: {convertedJson}");
        }

        public void ClearStoredMessages()
        {
            _messages.Clear();
        }

        public List<string> GetMessages()
        {
            return _messages;
        }
    }
}