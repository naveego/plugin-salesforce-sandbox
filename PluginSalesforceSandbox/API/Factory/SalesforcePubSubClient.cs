using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Grpc.Core;
using Grpc.Net.Client;
using Naveego.Sdk.Logging;
using Salesforce.PubSubApi;
using SolTechnology.Avro;

public class SalesforcePubSubClient
{
  private readonly PubSub.PubSubClient _client;
  private readonly Metadata _metadata;
  private readonly ConcurrentBag<string> _messages = new ConcurrentBag<string>();

  public SalesforcePubSubClient(string address, Metadata metadata)
  {
    var channelSalesforce = GrpcChannel.ForAddress(address);
    _client = new PubSub.PubSubClient(channelSalesforce);
    _metadata = metadata;
  }

  public TopicInfo GetTopicByName(string topicName)
  {
    Logger.Info($"Getting topic {topicName}");

    TopicRequest topicRequest = new TopicRequest
    {
      TopicName = topicName
    };

    return _client.GetTopic(request: topicRequest, headers: _metadata);
  }

  public SchemaInfo GetSchemaById(string schemaId)
  {
    Logger.Info($"Getting schema for {schemaId}");

    SchemaRequest schemaRequest = new SchemaRequest
    {
      SchemaId = schemaId
    };

    return _client.GetSchema(request: schemaRequest, headers: _metadata);
  }

  public async Task Subscribe(string topicName, string jsonSchema, CancellationTokenSource cts)
  {
    try
    {
      Logger.Info($"Subscribing to topic {topicName}");

      using AsyncDuplexStreamingCall<FetchRequest, FetchResponse> stream = _client.Subscribe(headers: _metadata, cancellationToken: cts.Token);

      FetchRequest fetchRequest = new FetchRequest
      {
        TopicName = topicName,
        ReplayPreset = ReplayPreset.Latest,
        NumRequested = 10
      };

      await WriteToStream(stream.RequestStream, fetchRequest);

      await ReadFromStream(stream.ResponseStream, jsonSchema, cts);

    }
    catch (RpcException e) when (e.StatusCode == StatusCode.Cancelled)
    {
      Logger.Error(e, $"Operation Cancelled: {e.Message} Source {e.Source} {e.StackTrace}");
      throw;
    }
  }

  public List<string> GetMessages()
  {
    var messages = _messages.ToList();
    ClearStoredMessages();
    return messages;
  }

  public void ClearStoredMessages()
  {
    _messages.Clear();
  }

  private async Task WriteToStream(IClientStreamWriter<FetchRequest> requestStream, FetchRequest fetchRequest)
  {
    await requestStream.WriteAsync(fetchRequest);
  }

  private async Task ReadFromStream(IAsyncStreamReader<FetchResponse> responseStream, string jsonSchema, CancellationTokenSource source = null)
  {
    while (await responseStream.MoveNext())
    {
      Logger.Info($"RPC ID: {responseStream.Current.RpcId}");

      if (responseStream.Current.Events != null)
      {
        Logger.Info($"Number of events received: {responseStream.Current.Events.Count}");
        foreach (var item in responseStream.Current.Events)
        {

          byte[] bytePayload = item.Event.Payload.ToByteArray();
          string jsonPayload = AvroConvert.Avro2Json(bytePayload, jsonSchema);
          Logger.Debug($"response: {jsonPayload}");

          _messages.Add(jsonPayload);
        }
      }
      else
      {
        Logger.Info($"Subscription is active");
      }
    }
  }
}