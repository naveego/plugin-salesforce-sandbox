using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Grpc.Core;
using LiteDB;
using Naveego.Sdk.Logging;
using Naveego.Sdk.Plugins;
using Newtonsoft.Json;
using PluginSalesforceSandbox.API.Factory;
using PluginSalesforceSandbox.DataContracts;
using PluginSalesforceSandbox.Helper;
using Salesforce.PubSubApi;

namespace PluginSalesforceSandbox.API.Read
{
    public static partial class Read
    {
        // constants
        private const string CollectionName = "realtimerecord";

        // connection client settings
        private static ISalesforcePubSubClientFactory _connectionFactory;
        private static RealTimeSettings _realTimeSettings;
        private static RequestHelper _requestHelper;

        // connection client
        private static CancellationTokenSource _cts;
        private static Task _subscribeTask;
        private static SalesforcePubSubClient _client;
        private static SchemaInfo _schemaInfo;
        private static TopicInfo _topicInfo;

        // state
        private static RealTimeState _realTimeState;

        public class RealTimeRecord
        {
            [BsonId] public string Id { get; set; }
            [BsonField] public string RunId { get; set; }
            [BsonField] public Dictionary<string, object> RecordKeysMap { get; set; }
            [BsonField] public byte[] RecordDataHash { get; set; }
        }

        public static async Task<int> ReadRecordsRealTimeAsync(RequestHelper requestHelper, ReadRequest request,
            IServerStreamWriter<Record> responseStream,
            ServerCallContext context, string permanentPath, ISalesforcePubSubClientFactory connectionFactory)
        {
            Logger.Info("Beginning to read records real time...");

            var schema = request.Schema;
            var jobVersion = request.DataVersions.JobDataVersion;
            var shapeVersion = request.DataVersions.ShapeDataVersion;
            var jobId = request.DataVersions.JobId;
            var shapeId = request.DataVersions.ShapeId;
            var recordsCount = 0;
            var runId = Guid.NewGuid().ToString();

            _realTimeSettings =
                JsonConvert.DeserializeObject<RealTimeSettings>(request.RealTimeSettingsJson);
            _realTimeState = !string.IsNullOrWhiteSpace(request.RealTimeStateJson)
                ? JsonConvert.DeserializeObject<RealTimeState>(request.RealTimeStateJson)
                : new RealTimeState();

            _requestHelper = requestHelper;
            _connectionFactory = connectionFactory;

            SubscribeSalesforcePubSubClient();

            try
            {
                // setup db directory
                var path = Path.Join(permanentPath, "realtime", jobId);
                Directory.CreateDirectory(path);

                Logger.Info("Real time read initializing...");

                using (var db = new LiteDatabase(Path.Join(path, $"{jobId}_RealTimeReadRecords.db")))
                {
                    var realtimeRecordsCollection = db.GetCollection<RealTimeRecord>(CollectionName);

                    // a full init needs to happen
                    if (jobVersion > _realTimeState.JobVersion || shapeVersion > _realTimeState.ShapeVersion)
                    {
                        // reset real time state
                        _realTimeState = new RealTimeState();
                        _realTimeState.JobVersion = jobVersion;
                        _realTimeState.ShapeVersion = shapeVersion;

                        // delete existing collection
                        realtimeRecordsCollection.DeleteAll();
                    }

                    // initialize job
                    var schemaKeys = new List<string>();

                    foreach (var property in schema.Properties)
                    {
                        if (property.IsKey)
                        {
                            schemaKeys.Add(property.Id);
                        }
                    }

                    var query = schema.Query;

                    // update real time state
                    _realTimeState.LastReadTime = DateTime.UtcNow;

                    await Initialize(schema, query, runId, schemaKeys, recordsCount,
                        realtimeRecordsCollection, responseStream);

                    Logger.Info("Real time read initialized.");

                    // run job until cancelled
                    while (!context.CancellationToken.IsCancellationRequested)
                    {
                        long currentRunRecordsCount = 0;

                        // process all messages since last batch interval
                        Logger.Debug($"Getting all records since {_realTimeState.LastReadTime.ToUniversalTime():O}");
                        var messages = _client.GetMessages();

                        foreach (var message in messages)
                        {
                            // publish record
                            var changeDataEvent = JsonConvert.DeserializeObject<ChangeDataEvent>(message);
                            var changeDataEventHeader = changeDataEvent.ChangeEventHeader;
                            recordsCount++;
                            currentRunRecordsCount++;

                            // cdc event only has partial records so get the full records from the API
                            var allRecords = GetRecordsForQuery(_requestHelper, Utility.Utility.GetSingleRecordsQuery(changeDataEventHeader.EntityName, changeDataEventHeader.RecordIds));

                            await foreach (var rawRecord in allRecords)
                            {
                                var recordMap = new Dictionary<string, object>();
                                var recordKeysMap = new Dictionary<string, object>();

                                MutateRecordMap(schema, rawRecord, recordMap, recordKeysMap);

                                var recordId = GetRecordKeyEntry(schemaKeys, recordMap);

                                switch (changeDataEventHeader.ChangeType.ToUpper())
                                {
                                    case "DELETE":
                                        // handle record deletion event
                                        Logger.Debug($"Deleting record {recordId}");

                                        var realtimeRecord =
                                            realtimeRecordsCollection.FindOne(r => r.Id == recordId);
                                        if (realtimeRecord == null)
                                        {
                                            Logger.Info($"Record {recordId} not found skipping delete event");
                                            continue;
                                        }

                                        realtimeRecordsCollection.DeleteMany(r =>
                                            r.Id == recordId);
                                        var deleteRecord = new Record
                                        {
                                            Action = Record.Types.Action.Delete,
                                            DataJson = JsonConvert.SerializeObject(recordMap)
                                        };
                                        await responseStream.WriteAsync(deleteRecord);
                                        recordsCount++;
                                        break;
                                    case "GAP_OVERFLOW":
                                    case "GAP_CREATE":
                                    case "GAP_UPDATE":
                                    case "GAP_DELETE":
                                    case "GAP_UNDELETE":
                                        // perform full init for GAP events
                                        runId = Guid.NewGuid().ToString();
                                        await Initialize(schema, query, runId, schemaKeys, recordsCount,
                                            realtimeRecordsCollection, responseStream);
                                        break;
                                    default:
                                        // handle record upsert event
                                        Logger.Debug($"Upserting record {recordId}");

                                        // build local db entry
                                        var recordChanged = UpsertRealTimeRecord(runId, schemaKeys, recordMap, recordKeysMap,
                                            realtimeRecordsCollection);

                                        if (recordChanged)
                                        {
                                            // Publish record
                                            var record = new Record
                                            {
                                                Action = Record.Types.Action.Upsert,
                                                DataJson = JsonConvert.SerializeObject(recordMap)
                                            };

                                            await responseStream.WriteAsync(record);
                                            recordsCount++;
                                        }
                                        break;
                                }
                            }
                        }

                        // update last read time
                        _realTimeState.LastReadTime = DateTime.Now;

                        // update real time state
                        var realTimeStateCommit = new Record
                        {
                            Action = Record.Types.Action.RealTimeStateCommit,
                            RealTimeStateJson = JsonConvert.SerializeObject(_realTimeState)
                        };
                        await responseStream.WriteAsync(realTimeStateCommit);

                        Logger.Debug(
                            $"Got {currentRunRecordsCount} records since {_realTimeState.LastReadTime.ToUniversalTime():O}");

                        if (_cts.IsCancellationRequested)
                        {
                            // reconnect after 30 minutes
                            Logger.Info("request to resubscribe stream requested");

                            SubscribeSalesforcePubSubClient();
                        }

                        // sleep until next check window
                        await Task.Delay(_realTimeSettings.BatchWindowSeconds * 1000, context.CancellationToken);
                    }
                }
            }
            catch (TaskCanceledException e)
            {
                Logger.Info($"Operation cancelled {e.Message}");
                return recordsCount;
            }
            catch (Exception e)
            {
                Logger.Error(e, e.Message, context);
                throw;
            }
            finally
            {
                _cts.Cancel();
            }

            return recordsCount;
        }

        private static void SubscribeSalesforcePubSubClient()
        {
            // build pub sub api client
            _client = _connectionFactory.GetPubSubClient(_requestHelper.GetToken(), _requestHelper.GetInstanceUrl(), _realTimeSettings.OrganizationId);

            // get schema info
            var topicName = _realTimeSettings.ChannelName;
            if (!topicName.StartsWith("/data/"))
            {
                topicName = $"/data/{_realTimeSettings.ChannelName}";
            }

            _topicInfo = _client.GetTopicByName(topicName);
            _schemaInfo = _client.GetSchemaById(_topicInfo.SchemaId);

            Logger.Debug(JsonConvert.SerializeObject(_topicInfo));
            Logger.Debug(JsonConvert.SerializeObject(_schemaInfo));

            // force the client to reconnect after 30 minutes
            _cts = new CancellationTokenSource();
            _cts.CancelAfter(1800 * 1000);

            // subscribe to topic
            _subscribeTask = Task.Run(async () => await _client.Subscribe(_topicInfo.TopicName, _schemaInfo.SchemaJson, _cts));
        }

        private static string GetRecordKeyEntry(List<string> schemaKeys, Dictionary<string, object> recordMap)
        {
            var entryIdStringList = new List<string>();

            foreach (var schemaKey in schemaKeys)
            {
                try
                {
                    var schemaKeyValue = recordMap[schemaKey];
                    entryIdStringList.Add($"{schemaKey}_{schemaKeyValue}");
                }
                catch (Exception e)
                {
                    Logger.Error(e, $"No column with property Id: {schemaKey}");
                    Logger.Error(e, e.Message);
                }
            }

            return string.Join('_', entryIdStringList);
        }

        private static byte[] GetRecordHash(Dictionary<string, object> recordMap)
        {
            using (var sha256 = SHA256.Create())
            {
                return sha256.ComputeHash(Encoding.Unicode.GetBytes(JsonConvert.SerializeObject(recordMap)));
            }
        }

        /// <summary>
        /// Upserts a record into the local db collection
        /// </summary>
        /// <param name="runId"></param>
        /// <param name="schemaKeys"></param>
        /// <param name="recordMap"></param>
        /// <param name="recordKeysMap"></param>
        /// <param name="realtimeRecordsCollection"></param>
        /// <returns>boolean indicating if record changed</returns>
        private static bool UpsertRealTimeRecord(string runId, List<string> schemaKeys,
            Dictionary<string, object> recordMap,
            Dictionary<string, object> recordKeysMap, ILiteCollection<RealTimeRecord> realtimeRecordsCollection)
        {
            // build local db entry
            var recordId = GetRecordKeyEntry(schemaKeys, recordMap);
            var recordHash = GetRecordHash(recordMap);

            // Create new real time record
            var realTimeRecord = new RealTimeRecord
            {
                Id = recordId,
                RunId = runId,
                RecordKeysMap = recordKeysMap,
                RecordDataHash = recordHash,
            };

            // get previous real time record
            var previousRealTimeRecord =
                realtimeRecordsCollection.FindById(GetRecordKeyEntry(schemaKeys, recordMap));

            // upsert new record into db
            realtimeRecordsCollection.Upsert(realTimeRecord);

            return previousRealTimeRecord == null ||
                   previousRealTimeRecord.RecordDataHash != realTimeRecord.RecordDataHash;
        }

        private static void MutateRecordMap(Schema schema, Dictionary<string, object> rawRecord,
            Dictionary<string, object> recordMap, Dictionary<string, object> recordKeysMap)
        {
            foreach (var property in schema.Properties)
            {
                try
                {
                    if (rawRecord.ContainsKey(property.Id))
                    {
                        if (rawRecord[property.Id] == null)
                        {
                            recordMap[property.Id] = null;
                            if (property.IsKey)
                            {
                                recordKeysMap[property.Id] = null;
                            }
                        }
                        else
                        {
                            if (property.Id == "LastModifiedDate" && (rawRecord[property.Id] is long || rawRecord[property.Id] is int))
                            {
                                var epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
                                long lastModifiedDate = (long)rawRecord[property.Id];
                                var dateTime = epoch.AddMilliseconds(lastModifiedDate);
                                rawRecord[property.Id] = dateTime.ToString("o");
                            }
                            switch (property.Type)
                            {
                                case PropertyType.String:
                                case PropertyType.Text:
                                case PropertyType.Decimal:
                                    recordMap[property.Id] =
                                        rawRecord[property.Id].ToString();
                                    if (property.IsKey)
                                    {
                                        recordKeysMap[property.Id] =
                                            rawRecord[property.Id].ToString();
                                    }
                                    break;
                                default:
                                    recordMap[property.Id] =
                                        rawRecord[property.Id];
                                    if (property.IsKey)
                                    {
                                        recordKeysMap[property.Id] =
                                            rawRecord[property.Id];
                                    }
                                    break;
                            }
                        }
                    }
                    else
                    {
                        recordMap[property.Id] = null;
                        if (property.IsKey)
                        {
                            recordKeysMap[property.Id] = null;
                        }
                    }
                }
                catch (Exception e)
                {
                    Logger.Error(e, $"No column with property Id: {property.Id}");
                    Logger.Error(e, e.Message);
                    recordMap[property.Id] = null;
                }
            }
        }

        /// <summary>
        /// Loads all data into the local db, uploads changed records, and deletes missing records
        /// </summary>
        /// <param name="schema"></param>
        /// <param name="query"></param>
        /// <param name="runId"></param>
        /// <param name="schemaKeys"></param>
        /// <param name="recordsCount"></param>
        /// <param name="realtimeRecordsCollection"></param>
        /// <param name="responseStream"></param>
        public static async Task Initialize(Schema schema, string query, string runId,
            List<string> schemaKeys, long recordsCount,
            ILiteCollection<RealTimeRecord> realtimeRecordsCollection, IServerStreamWriter<Record> responseStream)
        {
            IAsyncEnumerable<Dictionary<string, object>> allRecords;

            // get all records
            if (string.IsNullOrWhiteSpace(query))
            {
                allRecords = GetRecordsForDefaultQuery(_requestHelper, schema);
            }
            else
            {
                allRecords = GetRecordsForQuery(_requestHelper, query);
            }

            await foreach (var rawRecord in allRecords)
            {
                var recordMap = new Dictionary<string, object>();
                var recordKeysMap = new Dictionary<string, object>();

                // set values on recordMap and recordKeysMap
                MutateRecordMap(schema, rawRecord, recordMap, recordKeysMap);

                // build local db entry
                var recordChanged = UpsertRealTimeRecord(runId, schemaKeys, recordMap, recordKeysMap,
                    realtimeRecordsCollection);

                if (recordChanged)
                {
                    // Publish record
                    var record = new Record
                    {
                        Action = Record.Types.Action.Upsert,
                        DataJson = JsonConvert.SerializeObject(recordMap)
                    };

                    await responseStream.WriteAsync(record);
                    recordsCount++;
                }
            }

            // check for records that have been deleted
            var recordsToDelete = realtimeRecordsCollection.Find(r => r.RunId != runId);
            foreach (var realTimeRecord in recordsToDelete)
            {
                realtimeRecordsCollection.DeleteMany(r => r.Id == realTimeRecord.Id);
                var record = new Record
                {
                    Action = Record.Types.Action.Delete,
                    DataJson = JsonConvert.SerializeObject(realTimeRecord.RecordKeysMap)
                };
                await responseStream.WriteAsync(record);
                recordsCount++;
            }

            // commit real time state after completing the init
            var realTimeStateCommit = new Record
            {
                Action = Record.Types.Action.RealTimeStateCommit,
                RealTimeStateJson = JsonConvert.SerializeObject(_realTimeState)
            };

            await responseStream.WriteAsync(realTimeStateCommit);
        }
    }
}