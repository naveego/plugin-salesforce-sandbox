using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
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
        
        private static readonly string JournalQuery =
            @"SELECT JOCTRR, JOLIB, JOMBR, JOSEQN, JOENTT FROM {0}.{1} WHERE JOSEQN > {2} AND JOLIB = '{3}' AND JOMBR = '{4}' AND JOCODE = 'R'";

        private static readonly string MaxSeqQuery = @"select MAX(JOSEQN) as MAX_JOSEQN FROM {0}.{1}";

        private static readonly string RrnQuery = @"{0} {1} RRN({2}) = {3}";
        
        private const string CollectionName = "realtimerecord";

        public class RealTimeRecord
        {
            [BsonId] public string Id { get; set; }
            [BsonField] public Dictionary<string, object> Data { get; set; }
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
            var recordsCount = 0;

            // get base query
            var baseQuery = schema.Query;
            if (string.IsNullOrWhiteSpace(baseQuery))
            {
                baseQuery = Utility.Utility.GetDefaultQuery(schema);
            }

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

                    if (jobVersion > realTimeState.JobVersion)
                    {
                        realTimeState.LastReadTime = DateTime.MinValue;
                    }

                    // check to see if we need to load all the data
                    if (jobVersion > realTimeState.JobVersion || shapeVersion > realTimeState.ShapeVersion)
                    {
                        var rrnKeys = new List<string>();

                        foreach (var property in schema.Properties)
                        {
                            if (property.IsKey)
                            {
                                rrnKeys.Add(property.Id);
                            }
                        }

                        // delete existing collection
                        realtimeRecordsCollection.DeleteAll();

                        var queryDate = $"{realTimeState.LastReadTime.ToUniversalTime():yyyy-MM-dd}T{realTimeState.LastReadTime.ToUniversalTime():HH:mm:ss}Z";
                        

                        var query = schema.Query;

                        if (!string.IsNullOrEmpty(query))
                        {
                            if (query.ToUpper().Contains("WHERE"))
                            {
                                query += $" AND SystemModStamp >= {queryDate}";
                            }
                            else
                            {
                                query += $" WHERE SystemModStamp >= {queryDate}";
                            }
                        }
                        else
                        {
                            StringBuilder sbQuery = new StringBuilder("SELECT ");
                            foreach (var property in schema.Properties)
                            {
                                sbQuery.Append($"{property.Id}, ");
                            }
                            query = sbQuery.ToString();
                            
                            if (query.EndsWith(", "))
                            {
                                query = query.Substring(0, sbQuery.Length-2);
                            }
                            query += $" FROM {schema.Id} WHERE SystemModStamp >= {queryDate}";
                        }
                        
                        var reloadRecords = GetRecordsForQuery(client, schema, query);

                        await foreach (var reloadRecord in reloadRecords)
                        {
                            var recordMap = new Dictionary<string, object>();
                            var recordKeysMap = new Dictionary<string, object>();
                            foreach (var property in schema.Properties)
                            {
                                try
                                {
                                    if (reloadRecord.ContainsKey(property.Id))
                                    {
                                        switch (property.Type)
                                        {
                                            case PropertyType.String:
                                            case PropertyType.Text:
                                            case PropertyType.Decimal:
                                                recordMap[property.Id] =
                                                    reloadRecord[property.Id].ToString();
                                                if (property.IsKey)
                                                {
                                                    recordKeysMap[property.Id] =
                                                        reloadRecord[property.Id].ToString();
                                                }

                                                break;
                                            default:
                                                recordMap[property.Id] =
                                                    reloadRecord[property.Id];
                                                if (property.IsKey)
                                                {
                                                    recordKeysMap[property.Id] =
                                                        reloadRecord[property.Id];
                                                }

                                                break;
                                        }
                                    }
                                    else
                                    {
                                        recordMap[property.Id] = null;
                                    }
                                }
                                catch (Exception e)
                                {
                                    Logger.Error(e, $"No column with property Id: {property.Id}");
                                    Logger.Error(e, e.Message);
                                    recordMap[property.Id] = null;
                                }
                            }

                            // build local db entry
                            foreach (var rrnKey in rrnKeys)
                            {
                                try
                                {
                                    var rrn = reloadRecord[rrnKey];

                                    // Create new real time record
                                    var realTimeRecord = new RealTimeRecord
                                    {
                                        Id = $"{rrnKey}_{rrn}",
                                        Data = recordKeysMap
                                    };

                                    // Insert new record into db
                                    realtimeRecordsCollection.Upsert(realTimeRecord);
                                }
                                catch (Exception e)
                                {
                                    Logger.Error(e, $"No column with property Id: {rrnKey}");
                                    Logger.Error(e, e.Message);
                                }
                            }

                            // Publish record
                            var record = new Record
                            {
                                Action = Record.Types.Action.Upsert,
                                DataJson = JsonConvert.SerializeObject(recordMap)
                            };

                            await responseStream.WriteAsync(record);
                            recordsCount++;
                        }
                        // check for changes to process
                        if (conn.HasMessages())
                        {
                            await foreach (var message in conn.GetCurrentMessages())
                            {
                                // record map to send to response stream
                                var recordMap = new Dictionary<string, object>();
                                var recordKeysMap = new Dictionary<string, object>();

                                var realTimeEventWrapper = JsonConvert.DeserializeObject<RealTimeEventWrapper>(message);

                                foreach (var property in schema.Properties)
                                {
                                    try
                                    {
                                        if (realTimeEventWrapper.Data.SObject.ContainsKey(property.Id))
                                        {
                                            switch (property.Type)
                                            {
                                                case PropertyType.String:
                                                case PropertyType.Text:
                                                case PropertyType.Decimal:
                                                    recordMap[property.Id] =
                                                        realTimeEventWrapper.Data.SObject[property.Id].ToString();
                                                    if (property.IsKey)
                                                    {
                                                        recordKeysMap[property.Id] =
                                                            realTimeEventWrapper.Data.SObject[property.Id].ToString();
                                                    }
                                                    break;
                                                default:
                                                    recordMap[property.Id] =
                                                        realTimeEventWrapper.Data.SObject[property.Id];
                                                    if (property.IsKey)
                                                    {
                                                        recordKeysMap[property.Id] =
                                                            realTimeEventWrapper.Data.SObject[property.Id];
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

                                // build local db entry
                                foreach (var rrnKey in rrnKeys)
                                {
                                    try
                                    {
                                        var rrn = realTimeEventWrapper.Data.SObject[rrnKey];
                                        // Create new real time record
                                        var realTimeRecord = new RealTimeRecord
                                        {
                                            Id = $"{rrnKey}_{rrn}",
                                            Data = recordKeysMap
                                        };

                                        // Insert new record into db
                                        
                                        realtimeRecordsCollection.Upsert(realTimeRecord);
                                    }
                                    catch (Exception e)
                                    {
                                        Logger.Error(e, $"No column with property Id: {rrnKey}");
                                        Logger.Error(e, e.Message);
                                    }
                                }

                                var action = Record.Types.Action.Upsert; 
                                if (realTimeEventWrapper.Data.Event.Type.ToUpper() == "DELETED")
                                {
                                    action = Record.Types.Action.Delete;
                                }
                                
                                // Publish record
                                var record = new Record
                                {
                                    
                                    Action = action,
                                    DataJson = JsonConvert.SerializeObject(recordMap)
                                };

                                await responseStream.WriteAsync(record);
                                recordsCount++;
                            }
                        }

                        realTimeState.JobVersion = jobVersion;
                        realTimeState.ShapeVersion = shapeVersion;

                        var realTimeStateCommit = new Record
                        {
                            Action = Record.Types.Action.RealTimeStateCommit,
                            RealTimeStateJson = JsonConvert.SerializeObject(realTimeState)
                        };

                        await responseStream.WriteAsync(realTimeStateCommit);

                        Logger.Debug($"Got all records for reload");
                    }

                    Logger.Info("Real time read initialized.");
                    while (!context.CancellationToken.IsCancellationRequested)
                    {
                        long currentRunRecordsCount = 0;
                        Logger.Debug($"Getting all records since {realTimeState.LastReadTime.ToUniversalTime():O}");
                        var messages = conn.GetCurrentMessages();

                        
                        await foreach (var message in messages)
                        {
                            // publish record
                            var realTimeEventWrapper = JsonConvert.DeserializeObject<RealTimeEventWrapper>(message);
                            recordsCount++;
                            currentRunRecordsCount++;

                            var recordMap = new Dictionary<string, object>();

                            foreach (var property in schema.Properties)
                            {
                                if (realTimeEventWrapper.Data.SObject.ContainsKey(property.Id))
                                {
                                    try
                                    {
                                        switch (property.Type)
                                        {
                                            case PropertyType.String:
                                            case PropertyType.Text:
                                            case PropertyType.Decimal:
                                                recordMap[property.Id] = realTimeEventWrapper?.Data.SObject[property.Id].ToString();
                                                break;
                                            
                                            default:
                                                recordMap[property.Id] = realTimeEventWrapper?.Data.SObject[property.Id];
                                                break;
                                        }
                                    }
                                    catch (Exception e)
                                    {
                                        switch (property.Type)
                                        {
                                            case PropertyType.String:
                                            case PropertyType.Text:
                                            case PropertyType.Decimal:
                                                recordMap[property.Id] = "";
                                                break;
                                            
                                            default:
                                                recordMap[property.Id] = null;
                                                break;
                                        }
                                    }
                                   
                                    
                                }
                                else
                                {
                                    switch (property.Type)
                                    {
                                        case PropertyType.String:
                                        case PropertyType.Text:
                                        case PropertyType.Decimal:
                                            recordMap[property.Id] = "";
                                            break;
                                            
                                        default:
                                            recordMap[property.Id] = null;
                                            break;
                                    }
                                }
                            }

                            var recordId = recordMap["Id"].ToString();
                                
                            if (realTimeEventWrapper.Data.Event.Type.ToUpper() == "DELETED")
                            {
                                Logger.Info($"Deleting record {recordId}");

                                // handle record deletion
                                var realtimeRecord =
                                    realtimeRecordsCollection.FindOne(r => r.Id == recordId);
                                if (realtimeRecord == null)
                                {
                                    continue;
                                }
                                realtimeRecordsCollection.DeleteMany(r =>
                                    r.Id == recordId);
                                var record = new Record
                                {
                                    Action = Record.Types.Action.Delete,
                                    DataJson = JsonConvert.SerializeObject(recordMap)
                                };
                                await responseStream.WriteAsync(record);
                            }
                            else
                            {
                                Logger.Info($"Upserting record {recordId}");
                                
                                // Create new real time record
                                var recordKeysMap = new Dictionary<string, object>();
                                foreach (var property in schema.Properties)
                            {
                                try
                                {
                                    switch (property.Type)
                                    {
                                        case PropertyType.String:
                                        case PropertyType.Text:
                                        case PropertyType.Decimal:
                                            recordMap[property.Id] =
                                                realTimeEventWrapper?.Data.SObject[property.Id].ToString();
                                            if (property.IsKey)
                                            {
                                                recordKeysMap[property.Id] =
                                                    realTimeEventWrapper?.Data.SObject[property.Id].ToString();
                                            }

                                            break;
                                        default:
                                            recordMap[property.Id] =
                                                realTimeEventWrapper?.Data.SObject[property.Id];
                                            if (property.IsKey)
                                            {
                                                recordKeysMap[property.Id] =
                                                    realTimeEventWrapper?.Data.SObject[property.Id];
                                            }

                                            break;
                                    }

                                    // update local db
                                    var realTimeRecord = new RealTimeRecord
                                    {
                                        Id = recordId,
                                        Data = recordKeysMap
                                    };

                                    // upsert record into db
                                    realtimeRecordsCollection.Upsert(realTimeRecord);
                                }
                                catch (Exception e)
                                {
                                    Logger.Error(e,
                                        $"No column with property Id: {property.Id}");
                                    Logger.Error(e, e.Message);
                                    recordMap[property.Id] = null;
                                }
                            }
                                
                                var record = new Record
                                {
                                    Action = Record.Types.Action.Upsert,
                                    DataJson = JsonConvert.SerializeObject(recordMap)
                                };
                                await responseStream.WriteAsync(record);
                            }
                        }

                        conn.ClearStoredMessages();

                        realTimeState.LastReadTime = DateTime.Now;
                        realTimeState.JobVersion = jobVersion;

                        var realTimeStateCommit = new Record
                        {
                            Action = Record.Types.Action.RealTimeStateCommit,
                            RealTimeStateJson = JsonConvert.SerializeObject(realTimeState)
                        };
                        await responseStream.WriteAsync(realTimeStateCommit);

                        Logger.Debug(
                            $"Got {currentRunRecordsCount} records since {realTimeState.LastReadTime.ToUniversalTime():O}");

                        await Task.Delay(realTimeSettings.BatchWindow * 1000, context.CancellationToken);
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
    }
}