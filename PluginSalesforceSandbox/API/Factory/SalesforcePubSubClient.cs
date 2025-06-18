using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Google.Protobuf;
using Grpc.Core;
using Grpc.Net.Client;
using Naveego.Sdk.Logging;
using Salesforce.PubSubApi;
using SolTechnology.Avro;

public class SalesforcePubSubClient
{
  const int MAX_MESSAGES = 100;
  const int RESUBSCRIBE_THRESHOLD = 50;
  private readonly PubSub.PubSubClient _client;
  private readonly Metadata _metadata;
  private readonly ConcurrentQueue<string> _messages = new ConcurrentQueue<string>();
  private AsyncDuplexStreamingCall<FetchRequest, FetchResponse> _stream = null;
  private ByteString _replayId = ByteString.Empty;
  private int _messagesRemaining = MAX_MESSAGES;

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

      _stream = _client.Subscribe(headers: _metadata, cancellationToken: cts.Token);

      FetchRequest fetchRequest = new FetchRequest
      {
        TopicName = topicName,
        ReplayId = _replayId,
        ReplayPreset = Equals(ByteString.Empty, _replayId) ? ReplayPreset.Latest : ReplayPreset.Custom,
        NumRequested = 100
      };

      await WriteToStream(_stream.RequestStream, fetchRequest);

      await ReadFromStream(_stream.ResponseStream, topicName, jsonSchema, cts);
    }
    catch (RpcException e) when (e.StatusCode == StatusCode.Cancelled)
    {
      Logger.Error(e, $"Operation Cancelled: {e.Message} Source {e.Source} {e.StackTrace}");
      throw;
    }
  }

  public async Task Resubscribe(string topicName)
  {
    try
    {
      Logger.Info($"Resubscribing to topic {topicName}");

      FetchRequest fetchRequest = new FetchRequest
      {
        TopicName = topicName,
        ReplayPreset = ReplayPreset.Latest,
        NumRequested = MAX_MESSAGES
      };

      await WriteToStream(_stream.RequestStream, fetchRequest);
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

  private async Task ReadFromStream(IAsyncStreamReader<FetchResponse> responseStream, string topicName, string jsonSchema, CancellationTokenSource source = null)
  {
    while (await responseStream.MoveNext())
    {
      Logger.Info($"RPC ID: {responseStream.Current.RpcId}");

      if (responseStream.Current.Events != null)
      {
        _replayId = responseStream.Current.LatestReplayId;
        Logger.Info($"Number of events received: {responseStream.Current.Events.Count}, Replay ID: {_replayId}");
        foreach (var item in responseStream.Current.Events)
        {

          byte[] bytePayload = item.Event.Payload.ToByteArray();
          string jsonPayload = AvroConvert.Avro2Json(bytePayload, jsonSchema);
          Logger.Debug($"response: {jsonPayload}");

          _messages.Enqueue(jsonPayload);
          _messagesRemaining--;

          if (_messagesRemaining < RESUBSCRIBE_THRESHOLD)
          {
            await Resubscribe(topicName);
            _messagesRemaining = MAX_MESSAGES;
          }
        }
      }
      else
      {
        Logger.Info($"Subscription is active");
      }
    }
  }
}