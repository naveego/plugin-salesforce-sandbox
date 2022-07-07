using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Grpc.Core;
using LiteDB;
using Naveego.Sdk.Logging;
using Naveego.Sdk.Plugins;
using Newtonsoft.Json;
using PluginSalesforce.API.Factory;
using PluginSalesforce.DataContracts;
using PluginSalesforce.Helper;

namespace PluginSalesforce.API.Read
{
    public static partial class Read
    {
        private const string CollectionName = "realtimerecord";

        public class RealTimeRecord
        {
            [BsonId] public string Id { get; set; }
            [BsonField] public string RunId { get; set; }
            [BsonField] public Dictionary<string, object> RecordKeysMap { get; set; }
            [BsonField] public byte[] RecordDataHash { get; set; }
        }

        public static async Task<int> ReadRecordsRealTimeAsync(RequestHelper client, ReadRequest request,
            IServerStreamWriter<Record> responseStream,
            ServerCallContext context, string permanentPath, IPushTopicConnectionFactory connectionFactory)
        {
            Logger.Info("Beginning to read records real time...");

            var schema = request.Schema;
            var jobVersion = request.DataVersions.JobDataVersion;
            var shapeVersion = request.DataVersions.ShapeDataVersion;
            var jobId = request.DataVersions.JobId;
            var shapeId = request.DataVersions.ShapeId;
            var recordsCount = 0;
            var runId = Guid.NewGuid().ToString();

            var realTimeSettings =
                JsonConvert.DeserializeObject<RealTimeSettings>(request.RealTimeSettingsJson);
            var realTimeState = !string.IsNullOrWhiteSpace(request.RealTimeStateJson)
                ? JsonConvert.DeserializeObject<RealTimeState>(request.RealTimeStateJson)
                : new RealTimeState();

            var conn = connectionFactory.GetPushTopicConnection(client, @"/topic/" + realTimeSettings.ChannelName);

            try
            {
                // setup db directory
                var path = Path.Join(permanentPath, "realtime", jobId);
                Directory.CreateDirectory(path);

                Logger.Info("Real time read initializing...");

                using (var db = new LiteDatabase(Path.Join(path, $"{jobId}_RealTimeReadRecords.db")))
                {
                    var realtimeRecordsCollection = db.GetCollection<RealTimeRecord>(CollectionName);

                    // build cometd client
                    conn.Connect();

                    // a full init needs to happen
                    if (jobVersion > realTimeState.JobVersion || shapeVersion > realTimeState.ShapeVersion)
                    {
                        // reset real time state
                        realTimeState = new RealTimeState();
                        realTimeState.JobVersion = jobVersion;
                        realTimeState.ShapeVersion = shapeVersion;

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
                    if (string.IsNullOrWhiteSpace(query))
                    {
                        query = Utility.Utility.GetDefaultQuery(schema);
                    }

                    // update real time state
                    realTimeState.LastReadTime = DateTime.UtcNow;

                    await Initialize(client, schema, query, runId, realTimeState, schemaKeys, recordsCount,
                        realtimeRecordsCollection, responseStream);

                    Logger.Info("Real time read initialized.");
                    
                    // run job until cancelled
                    while (!context.CancellationToken.IsCancellationRequested)
                    {
                        long currentRunRecordsCount = 0;

                        // process all messages since last batch interval
                        Logger.Debug($"Getting all records since {realTimeState.LastReadTime.ToUniversalTime():O}");
                        var messages = conn.GetCurrentMessages();

                        await foreach (var message in messages)
                        {
                            // publish record
                            var realTimeEventWrapper = JsonConvert.DeserializeObject<RealTimeEventWrapper>(message);
                            recordsCount++;
                            currentRunRecordsCount++;

                            var recordMap = new Dictionary<string, object>();
                            var recordKeysMap = new Dictionary<string, object>();

                            MutateRecordMap(schema, realTimeEventWrapper.Data.SObject, recordMap, recordKeysMap);

                            var recordId = GetRecordKeyEntry(schemaKeys, recordMap);

                            switch (realTimeEventWrapper.Data.Event.Type.ToUpper())
                            {
                                case "DELETED":
                                    // handle record deletion event
                                    Logger.Debug($"Deleting record {recordId}");
                                    
                                    var realtimeRecord =
                                        realtimeRecordsCollection.FindOne(r => r.Id == recordId);
                                    if (realtimeRecord == null)
                                    {
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
                                    break;
                                case "GAP_OVERFLOW":
                                case "GAP_CREATE":
                                case "GAP_UPDATE":
                                case "GAP_DELETE":
                                case "GAP_UNDELETE":
                                    // perform full init for GAP events
                                    runId = Guid.NewGuid().ToString();
                                    await Initialize(client, schema, query, runId, realTimeState, schemaKeys, recordsCount,
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

                        // clear processed messages
                        conn.ClearStoredMessages();

                        // update last read time
                        realTimeState.LastReadTime = DateTime.Now;

                        // update real time state
                        var realTimeStateCommit = new Record
                        {
                            Action = Record.Types.Action.RealTimeStateCommit,
                            RealTimeStateJson = JsonConvert.SerializeObject(realTimeState)
                        };
                        await responseStream.WriteAsync(realTimeStateCommit);

                        Logger.Debug(
                            $"Got {currentRunRecordsCount} records since {realTimeState.LastReadTime.ToUniversalTime():O}");

                        await Task.Delay(realTimeSettings.BatchWindowSeconds * 1000, context.CancellationToken);
                    }
                }
            }
            catch (TaskCanceledException e)
            {
                Logger.Info($"Operation cancelled {e.Message}");
                conn.Disconnect();
                return recordsCount;
            }
            catch (Exception e)
            {
                Logger.Error(e, e.Message, context);
                throw;
            }
            finally
            {
                conn.Disconnect();
            }

            return recordsCount;
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
                RecordDataHash = recordHash
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
        /// <param name="client"></param>
        /// <param name="schema"></param>
        /// <param name="query"></param>
        /// <param name="runId"></param>
        /// <param name="realTimeState"></param>
        /// <param name="schemaKeys"></param>
        /// <param name="recordsCount"></param>
        /// <param name="realtimeRecordsCollection"></param>
        /// <param name="responseStream"></param>
        public static async Task Initialize(RequestHelper client, Schema schema, string query, string runId,
            RealTimeState realTimeState, List<string> schemaKeys, long recordsCount,
            ILiteCollection<RealTimeRecord> realtimeRecordsCollection, IServerStreamWriter<Record> responseStream)
        {
            // get all records
            var allRecords = GetRecordsForQuery(client, schema, query);

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
            }

            // commit real time state after completing the init
            var realTimeStateCommit = new Record
            {
                Action = Record.Types.Action.RealTimeStateCommit,
                RealTimeStateJson = JsonConvert.SerializeObject(realTimeState)
            };

            await responseStream.WriteAsync(realTimeStateCommit);
        }
    }
}