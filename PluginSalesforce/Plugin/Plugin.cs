using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Naveego.Sdk.Logging;
using Naveego.Sdk.Plugins;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using PluginSalesforce.API.Discover;
using PluginSalesforce.API.Read;
using PluginSalesforce.DataContracts;
using PluginSalesforce.Helper;


namespace PluginSalesforce.Plugin
{
    public class Plugin : Publisher.PublisherBase
    {
        private RequestHelper _client;
        private readonly HttpClient _injectedClient;
        private readonly ServerStatus _server;
        private TaskCompletionSource<bool> _tcs;
        private ConcurrentDictionary<string, List<FieldObject>> _fieldObjectsDictionary;

        public Plugin(HttpClient client = null)
        {
            _injectedClient = client ?? new HttpClient();
            _server = new ServerStatus
            {
                Connected = false,
                WriteConfigured = false
            };
            _fieldObjectsDictionary = new ConcurrentDictionary<string, List<FieldObject>>();
        }

        /// <summary>
        /// Creates an authorization url for oauth requests
        /// </summary>
        /// <param name="request"></param>
        /// <param name="context"></param>
        /// <returns></returns>
        public override Task<BeginOAuthFlowResponse> BeginOAuthFlow(BeginOAuthFlowRequest request,
            ServerCallContext context)
        {
            Logger.Info("Getting Auth URL...");

            // params for auth url
            var clientId = request.Configuration.ClientId;
            var responseType = "code";
            var redirectUrl = request.RedirectUrl;
            var prompt = "consent";
            var display = "popup";

            // build auth url
            var authUrl = String.Format(
                "https://login.salesforce.com/services/oauth2/authorize?client_id={0}&response_type={1}&redirect_uri={2}&prompt={3}&display={4}",
                clientId,
                responseType,
                redirectUrl,
                prompt,
                display);

            // return auth url
            var oAuthResponse = new BeginOAuthFlowResponse
            {
                AuthorizationUrl = authUrl
            };

            Logger.Info($"Created Auth URL: {authUrl}");

            return Task.FromResult(oAuthResponse);
        }

        /// <summary>
        /// Gets auth token and refresh tokens from auth code
        /// </summary>
        /// <param name="request"></param>
        /// <param name="context"></param>
        /// <returns></returns>
        public override async Task<CompleteOAuthFlowResponse> CompleteOAuthFlow(CompleteOAuthFlowRequest request,
            ServerCallContext context)
        {
            Logger.Info("Getting Auth and Refresh Token...");
            Logger.Info($"Redirect url: {request.RedirectUrl}");

            // get code from redirect url
            string code;
            var uri = new Uri(request.RedirectUrl);

            try
            {
                code = HttpUtility.UrlDecode(HttpUtility.ParseQueryString(uri.Query).Get("code"));
            }
            catch (Exception e)
            {
                Logger.Error(e, e.Message, context);
                throw;
            }

            // token url parameters
            var redirectUrl = String.Format("{0}{1}{2}{3}", uri.Scheme, Uri.SchemeDelimiter, uri.Authority,
                uri.AbsolutePath);
            var clientId = request.Configuration.ClientId;
            var clientSecret = request.Configuration.ClientSecret;
            var grantType = "authorization_code";

            // build token url
            var tokenUrl = "https://login.salesforce.com/services/oauth2/token";

            // build form data request
            var formData = new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>("grant_type", grantType),
                new KeyValuePair<string, string>("client_id", clientId),
                new KeyValuePair<string, string>("client_secret", clientSecret),
                new KeyValuePair<string, string>("redirect_uri", redirectUrl),
                new KeyValuePair<string, string>("code", code)
            };

            var body = new FormUrlEncodedContent(formData);

            // get tokens
            var oAuthState = new OAuthState();
            try
            {
                var client = _injectedClient;
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

                var response = await client.PostAsync(tokenUrl, body);

                if (!response.IsSuccessStatusCode)
                {
                    Logger.Info(await response.Content.ReadAsStringAsync());
                    response.EnsureSuccessStatusCode();
                }

                var content = JsonConvert.DeserializeObject<TokenResponse>(await response.Content.ReadAsStringAsync());

                oAuthState.AuthToken = content.AccessToken;
                oAuthState.RefreshToken = content.RefreshToken;
                oAuthState.Config = JsonConvert.SerializeObject(new OAuthConfig
                {
                    InstanceUrl = content.InstanceUrl
                });

                if (String.IsNullOrEmpty(oAuthState.RefreshToken))
                {
                    throw new Exception("Response did not contain a refresh token");
                }

                if (String.IsNullOrEmpty(content.InstanceUrl))
                {
                    throw new Exception("Response did not contain an instance url");
                }
            }
            catch (Exception e)
            {
                Logger.Error(e, e.Message, context);
                throw;
            }

            // return oauth state json
            var oAuthResponse = new CompleteOAuthFlowResponse
            {
                OauthStateJson = JsonConvert.SerializeObject(oAuthState)
            };

            Logger.Info("Got Auth Token and Refresh Token");

            return oAuthResponse;
        }

        /// <summary>
        /// Configures the plugin
        /// </summary>
        /// <param name="request"></param>
        /// <param name="context"></param>
        /// <returns></returns>
        public override Task<ConfigureResponse> Configure(ConfigureRequest request, ServerCallContext context)
        {
            Logger.Debug("Got configure request");
            Logger.Debug(JsonConvert.SerializeObject(request, Formatting.Indented));

            // ensure all directories are created
            Directory.CreateDirectory(request.TemporaryDirectory);
            Directory.CreateDirectory(request.PermanentDirectory);
            Directory.CreateDirectory(request.LogDirectory);


            // configure logger
            Logger.SetLogLevel(request.LogLevel);
            Logger.Init(request.LogDirectory);

            _server.Config = request;

            return Task.FromResult(new ConfigureResponse());
        }

        /// <summary>
        /// Establishes a connection with a Salesforce instance. Creates an authenticated http client and tests it.
        /// </summary>
        /// <param name="request"></param>
        /// <param name="context"></param>
        /// <returns>A message indicating connection success</returns>
        public override async Task<ConnectResponse> Connect(ConnectRequest request, ServerCallContext context)
        {
            _server.Connected = false;

            Logger.Info("Connecting...");
            Logger.Info(JsonConvert.SerializeObject(request, Formatting.Indented));
//            Logger.Info("Got OAuth State: " + request.OauthStateJson);
//            Logger.Info("Got OAuthConfig " + JsonConvert.SerializeObject(request.OauthConfiguration));

            OAuthState oAuthState;
            OAuthConfig oAuthConfig;
            try
            {
                oAuthState = JsonConvert.DeserializeObject<OAuthState>(request.OauthStateJson);
                oAuthConfig = JsonConvert.DeserializeObject<OAuthConfig>(oAuthState.Config);
            }
            catch (Exception e)
            {
                Logger.Error(e, e.Message);
                return new ConnectResponse
                {
                    OauthStateJson = request.OauthStateJson,
                    ConnectionError = "",
                    OauthError = e.Message,
                    SettingsError = ""
                };
            }

            var settings = new Settings
            {
                ClientId = request.OauthConfiguration.ClientId,
                ClientSecret = request.OauthConfiguration.ClientSecret,
                RefreshToken = oAuthState.RefreshToken,
                InstanceUrl = oAuthConfig.InstanceUrl
            };

            // validate settings passed in
            try
            {
                _server.Settings = settings;
                _server.Settings.Validate();
            }
            catch (Exception e)
            {
                Logger.Error(e, e.Message);
                return new ConnectResponse
                {
                    OauthStateJson = request.OauthStateJson,
                    ConnectionError = "",
                    OauthError = "",
                    SettingsError = e.Message
                };
            }

            // create new authenticated request helper with validated settings
            try
            {
                _client = new RequestHelper(_server.Settings, _injectedClient);
            }
            catch (Exception e)
            {
                Logger.Error(e, e.Message);
                return new ConnectResponse
                {
                    OauthStateJson = request.OauthStateJson,
                    ConnectionError = "",
                    OauthError = "",
                    SettingsError = e.Message
                };
            }

            // attempt to call the Salesforce api
            try
            {
                var response = await _client.GetAsync("/tabs");
                if (!response.IsSuccessStatusCode)
                {
                    var body = await response.Content.ReadAsStringAsync();
                    var message = $"Call to /tabs failed with status {response.StatusCode}: {body}";
                    Logger.Error(null, body);
                    return new ConnectResponse
                    {
                        OauthStateJson = request.OauthStateJson,
                        ConnectionError = "desc = " + body,
                        OauthError = "",
                        SettingsError = ""
                    };
                }

                _server.Connected = true;

                Logger.Info("Connected to Salesforce");
            }
            catch (Exception e)
            {
                Logger.Error(e, e.Message);

                return new ConnectResponse
                {
                    OauthStateJson = request.OauthStateJson,
                    ConnectionError = e.Message,
                    OauthError = "",
                    SettingsError = ""
                };
            }

            return new ConnectResponse
            {
                OauthStateJson = request.OauthStateJson,
                ConnectionError = "",
                OauthError = "",
                SettingsError = ""
            };
        }

        public override async Task ConnectSession(ConnectRequest request,
            IServerStreamWriter<ConnectResponse> responseStream, ServerCallContext context)
        {
            Logger.Info("Connecting session...");

            // create task to wait for disconnect to be called
            _tcs?.SetResult(true);
            _tcs = new TaskCompletionSource<bool>();

            // call connect method
            var response = await Connect(request, context);

            await responseStream.WriteAsync(response);

            Logger.Info("Session connected.");

            // wait for disconnect to be called
            await _tcs.Task;
        }


        /// <summary>
        /// Discovers schemas located in the Salesforce instance
        /// </summary>
        /// <param name="request"></param>
        /// <param name="context"></param>
        /// <returns>Discovered schemas</returns>
        public override async Task<DiscoverSchemasResponse> DiscoverSchemas(DiscoverSchemasRequest request,
            ServerCallContext context)
        {
            Logger.SetLogPrefix("discover");
            Logger.Info("Discovering Schemas...");

            DiscoverSchemasResponse discoverSchemasResponse = new DiscoverSchemasResponse();

            // handle query based schema
            try
            {
                if (request.Mode == DiscoverSchemasRequest.Types.Mode.Refresh && request.ToRefresh.Count == 1 &&
                    !string.IsNullOrWhiteSpace(request.ToRefresh.First().Query))
                {
                    discoverSchemasResponse.Schemas.Add(await Discover.GetSchemaForQuery(_injectedClient, request.ToRefresh.First()));
                    return discoverSchemasResponse;
                }
            }
            catch (Exception e)
            {
                Logger.Error(e, e.Message, context);
                return new DiscoverSchemasResponse();
            }

            List<TabObject> tabsResponse;
            // get the tabs present in Salesforce
            try
            {
                Logger.Debug("Getting tabs...");
                var response = await _client.GetAsync("/tabs");
                response.EnsureSuccessStatusCode();

                tabsResponse =
                    JsonConvert.DeserializeObject<List<TabObject>>(await response.Content.ReadAsStringAsync());
            }
            catch (Exception e)
            {
                Logger.Error(e, e.Message, context);
                return new DiscoverSchemasResponse();
            }

            // attempt to get a schema for each tab found
            try
            {
                Logger.Info($"Schemas attempted: {tabsResponse.Count}");

                var tasks = tabsResponse
                    .Select(t => Discover.GetSchemaForTab(_injectedClient, _fieldObjectsDictionary, t))
                    .ToArray();

                await Task.WhenAll(tasks);

                discoverSchemasResponse.Schemas.AddRange(tasks.Where(x => x.Result != null).Select(x => x.Result));
            }
            catch (Exception e)
            {
                Logger.Error(e, e.Message, context);
                return new DiscoverSchemasResponse();
            }

            Logger.Info($"Schemas found: {discoverSchemasResponse.Schemas.Count}");

            // only return requested schemas if refresh mode selected
            if (request.Mode == DiscoverSchemasRequest.Types.Mode.Refresh)
            {
                var refreshSchemas = request.ToRefresh;
                var schemas =
                    JsonConvert.DeserializeObject<Schema[]>(
                        JsonConvert.SerializeObject(discoverSchemasResponse.Schemas));
                discoverSchemasResponse.Schemas.Clear();
                discoverSchemasResponse.Schemas.AddRange(schemas.Join(refreshSchemas, schema => schema.Id,
                    refreshSchema => refreshSchema.Id,
                    (schema, refresh) => schema));

                Logger.Debug($"Schemas found: {JsonConvert.SerializeObject(schemas)}");
                Logger.Debug($"Refresh requested on schemas: {JsonConvert.SerializeObject(refreshSchemas)}");

                Logger.Info($"Schemas returned: {discoverSchemasResponse.Schemas.Count}");
                return discoverSchemasResponse;
            }

            // return all schemas otherwise
            Logger.Info($"Schemas returned: {discoverSchemasResponse.Schemas.Count}");
            return discoverSchemasResponse;
        }

        /// <summary>
        /// Publishes a stream of data for a given schema
        /// </summary>
        /// <param name="request"></param>
        /// <param name="responseStream"></param>
        /// <param name="context"></param>
        /// <returns></returns>
        public override async Task ReadStream(ReadRequest request, IServerStreamWriter<Record> responseStream,
            ServerCallContext context)
        {
            var schema = request.Schema;
            var limit = request.Limit;
            var limitFlag = request.Limit != 0;

            Logger.SetLogPrefix(request.JobId);
            Logger.Info($"Publishing records for schema: {schema.Name}");

            try
            {
                var recordsCount = 0;
                var records = new List<Dictionary<string, object>>();

                if (!string.IsNullOrWhiteSpace(schema.Query))
                {
                    await foreach (var record in Read.GetRecordsForQuery(_injectedClient, schema))
                    {
                        records.Add(record);

                        if (records.Count % 100 == 0)
                        {
                            recordsCount =
                                await PublishRecords(schema, limitFlag, limit, records, recordsCount, responseStream, true);
                        }
                        records.Clear();
                    }
                    
                    recordsCount =
                        await PublishRecords(schema, limitFlag, limit, records, recordsCount, responseStream, true);
                    
                    Logger.Info($"Published {recordsCount} records");
                }
                else
                {
                    // get all records
                    // build query string
                    var query = $@"select fields(all) from {schema.Id} order by CreatedDate asc nulls last limit 200";

                    // get records for schema page by page
                    RecordsResponse recordsResponse;
                    DateTime previousDate;
                    DateTime? createdDate = DateTime.Now;
                    do
                    {
                        previousDate = createdDate.GetValueOrDefault();

                        // get records
                        var response = await _client.GetAsync($"/query?q={HttpUtility.UrlEncode(query)}");
                        response.EnsureSuccessStatusCode();

                        recordsResponse =
                            JsonConvert.DeserializeObject<RecordsResponse>(await response.Content.ReadAsStringAsync());

                        records.AddRange(recordsResponse.Records);

                        // Publish records for the given schema
                        recordsCount =
                            await PublishRecords(schema, limitFlag, limit, records, recordsCount, responseStream);

                        // update query
                        createdDate = (DateTime?) records.LastOrDefault()?["CreatedDate"];
                        query =
                            $@"select fields(all) from {schema.Id} where CreatedDate >= {(createdDate.HasValue ? createdDate.Value.ToUniversalTime().ToString("O") : "")} order by CreatedDate asc nulls last limit 200";

                        // clear records
                        records.Clear();
                    } while (previousDate != createdDate.GetValueOrDefault() && recordsResponse.TotalSize == 200 &&
                             _server.Connected);

                    _allRecordIds.Clear();

                    Logger.Info($"Published {recordsCount} records");
                }
            }
            catch (Exception e)
            {
                Logger.Error(e, e.Message, context);
            }
        }

        private readonly List<string> _allRecordIds = new List<string>();

        private async Task<int> PublishRecords(Schema schema, bool limitFlag, uint limit,
            List<Dictionary<string, object>> records, int recordsCount, IServerStreamWriter<Record> responseStream, bool forQuery = false)
        {
            // Publish records for the given schema
            foreach (var record in records)
            {
                try
                {
                    record.Remove("attributes");

                    foreach (var property in schema.Properties)
                    {
                        if (property.Type == PropertyType.String)
                        {
                            var value = record[property.Id];
                            if (!(value is string))
                            {
                                record[property.Id] = JsonConvert.SerializeObject(value);
                            }
                        }
                    }
                }
                catch (Exception e)
                {
                    Logger.Error(e, e.Message);
                    continue;
                }

                var recordOutput = new Record
                {
                    Action = Record.Types.Action.Upsert,
                    DataJson = JsonConvert.SerializeObject(record)
                };

                // stop publishing if the limit flag is enabled and the limit has been reached
                if ((limitFlag && recordsCount == limit) || !_server.Connected)
                {
                    break;
                }

                // publish record
                if (forQuery)
                {
                    await responseStream.WriteAsync(recordOutput);
                    recordsCount++;
                }
                else
                {
                    if (!_allRecordIds.Contains(record["Id"]))
                    {
                        _allRecordIds.Add(record["Id"]?.ToString());
                        await responseStream.WriteAsync(recordOutput);
                        recordsCount++;
                    }
                }
            }

            return recordsCount;
        }

        /// <summary>
        /// Prepares the plugin to handle a write request
        /// </summary>
        /// <param name="request"></param>
        /// <param name="context"></param>
        /// <returns></returns>
        public override async Task<PrepareWriteResponse> PrepareWrite(PrepareWriteRequest request,
            ServerCallContext context)
        {
            Logger.SetLogPrefix(request.DataVersions.JobId);
            Logger.Info("Preparing write...");
            _server.WriteConfigured = false;

            var writeSettings = new WriteSettings
            {
                CommitSLA = request.CommitSlaSeconds,
                Schema = request.Schema
            };

            // get fields for module
            var response = await _client.GetAsync(String.Format("/sobjects/{0}/describe", request.Schema.Id));

            // for each field in the schema add a new property
            var describeResponse =
                JsonConvert.DeserializeObject<DescribeResponse>(await response.Content.ReadAsStringAsync());

            _fieldObjectsDictionary.TryAdd(request.Schema.Id, describeResponse.Fields);

            _server.WriteSettings = writeSettings;
            _server.WriteConfigured = true;

            Logger.Info("Write prepared.");
            return new PrepareWriteResponse();
        }

        /// <summary>
        /// Takes in records and writes them out to Salesforce then sends acks back to the client
        /// </summary>
        /// <param name="requestStream"></param>
        /// <param name="responseStream"></param>
        /// <param name="context"></param>
        /// <returns></returns>
        public override async Task WriteStream(IAsyncStreamReader<Record> requestStream,
            IServerStreamWriter<RecordAck> responseStream, ServerCallContext context)
        {
            try
            {
                Logger.Info("Writing records to Salesforce...");
                var schema = _server.WriteSettings.Schema;
                var sla = _server.WriteSettings.CommitSLA;
                var inCount = 0;
                var outCount = 0;

                // get next record to publish while connected and configured
                while (await requestStream.MoveNext(context.CancellationToken) && _server.Connected &&
                       _server.WriteConfigured)
                {
                    var record = requestStream.Current;
                    inCount++;

                    Logger.Debug($"Got record: {record.DataJson}");

                    // send record to source system
                    // timeout if it takes longer than the sla
                    Task<string> task;

                    if (record.Action == Record.Types.Action.Delete)
                    {
                        task = Task.Run(() => DeleteRecord(schema, record));
                    }
                    else
                    {
                        task = Task.Run(() => PutRecord(schema, record));
                    }

                    if (task.Wait(TimeSpan.FromSeconds(sla)))
                    {
                        // send ack
                        var ack = new RecordAck
                        {
                            CorrelationId = record.CorrelationId,
                            Error = task.Result
                        };
                        await responseStream.WriteAsync(ack);

                        if (String.IsNullOrEmpty(task.Result))
                        {
                            outCount++;
                        }
                    }
                    else
                    {
                        // send timeout ack
                        var ack = new RecordAck
                        {
                            CorrelationId = record.CorrelationId,
                            Error = "timed out"
                        };
                        await responseStream.WriteAsync(ack);
                    }
                }

                Logger.Info($"Wrote {outCount} of {inCount} records to Salesforce.");
            }
            catch (Exception e)
            {
                Logger.Error(e, e.Message, context);
            }
        }

        /// <summary>
        /// Handles disconnect requests from the agent
        /// </summary>
        /// <param name="request"></param>
        /// <param name="context"></param>
        /// <returns></returns>
        public override Task<DisconnectResponse> Disconnect(DisconnectRequest request, ServerCallContext context)
        {
            // clear connection
            _server.Connected = false;
            _server.Settings = null;

            // alert connection session to close
            if (_tcs != null)
            {
                _tcs.SetResult(true);
                _tcs = null;
            }

            Logger.Info("Disconnected");
            return Task.FromResult(new DisconnectResponse());
        }

        /// <summary>
        /// Writes a record out to Salesforce
        /// </summary>
        /// <param name="schema"></param>
        /// <param name="record"></param>
        /// <returns></returns>
        private async Task<string> PutRecord(Schema schema, Record record)
        {
            try
            {
                // record has id and exists
                // check if source has newer record than write back record
                var recObj = JsonConvert.DeserializeObject<Dictionary<string, object>>(record.DataJson);

                var key = schema.Properties.First(x => x.IsKey);

                if (recObj.ContainsKey(key.Id))
                {
                    var id = recObj[key.Id];

                    if (id != null)
                    {
                        // build and send request
                        var uri = String.Format("/sobjects/{0}/{1}", schema.Id, id);

                        var response = await _client.GetAsync(uri);
                        response.EnsureSuccessStatusCode();

                        var srcObj =
                            JsonConvert.DeserializeObject<Dictionary<string, object>>(
                                await response.Content.ReadAsStringAsync());

                        // get modified key from schema
                        var modifiedKey = schema.Properties.First(x => x.IsUpdateCounter);

                        if (recObj.ContainsKey(modifiedKey.Id) && srcObj.ContainsKey(modifiedKey.Id))
                        {
                            if (recObj[modifiedKey.Id] != null && srcObj[modifiedKey.Id] != null)
                            {
                                // if source is newer than request then exit
                                if (DateTime.Parse(recObj[modifiedKey.Id].ToString()) <
                                    DateTime.Parse(srcObj[modifiedKey.Id].ToString()))
                                {
                                    Logger.Info($"Source is newer for record {record.DataJson}");
                                    return "source system is newer than requested write back";
                                }
                            }
                        }

                        // build and send request
                        uri = String.Format("/sobjects/{0}/{1}", schema.Id, id);

                        var patchObj = GetPatchObject(schema, recObj);

                        var content = new StringContent(JsonConvert.SerializeObject(patchObj), Encoding.UTF8,
                            "application/json");

                        response = await _client.PatchAsync(uri, content);
                        response.EnsureSuccessStatusCode();

                        Logger.Info("Modified 1 record.");
                        return "";
                    }
                    else
                    {
                        // record does not have id and needs to be created
                        var uri = String.Format("/sobjects/{0}", schema.Id);

                        var patchObj = GetPatchObject(schema, recObj);

                        var content = new StringContent(JsonConvert.SerializeObject(patchObj), Encoding.UTF8,
                            "application/json");

                        var response = await _client.PostAsync(uri, content);
                        response.EnsureSuccessStatusCode();

                        Logger.Info("Created 1 record.");
                        return "";
                    }
                }

                return $"Key {key.Id} not found on requested record to write back.";
            }
            catch (Exception e)
            {
                Logger.Error(e, e.Message);
                return e.Message;
            }
        }

        /// <summary>
        /// Deletes a record from Salesforce
        /// </summary>
        /// <param name="schema"></param>
        /// <param name="record"></param>
        /// <returns></returns>
        private async Task<string> DeleteRecord(Schema schema, Record record)
        {
            try
            {
                var recObj = JsonConvert.DeserializeObject<Dictionary<string, object>>(record.DataJson);

                var key = schema.Properties.First(x => x.IsKey);

                if (recObj.ContainsKey(key.Id))
                {
                    if (recObj[key.Id] != null)
                    {
                        // delete record
                        // build and send request
                        var uri = String.Format("/sobjects/{0}/{1}", schema.Id, recObj[key.Id]);

                        var response = await _client.DeleteAsync(uri);
                        response.EnsureSuccessStatusCode();

                        Logger.Info("Deleted 1 record.");
                        return "";
                    }

                    return "Could not delete record with no id.";
                }

                return "Key not found on requested record to write back.";
            }
            catch (Exception e)
            {
                Logger.Error(e, e.Message);
                return e.Message;
            }
        }

        /// <summary>
        /// Gets the patch object to send to the update endpoint
        /// </summary>
        /// <param name="schema"></param>
        /// <param name="recObj"></param>
        /// <returns></returns>
        private Dictionary<string, object> GetPatchObject(Schema schema, Dictionary<string, object> recObj)
        {
            try
            {
                var patchObj = new Dictionary<string, object>();

                if (_fieldObjectsDictionary.TryGetValue(schema.Id, out List<FieldObject> fields))
                {
                    foreach (var property in schema.Properties)
                    {
                        var fieldObj = fields.Find(f => f.Name == property.Id);

                        if (fieldObj.Updateable)
                        {
                            if (recObj.ContainsKey(property.Id))
                            {
                                if (property.Type == PropertyType.String)
                                {
                                    if (recObj[property.Id] != null)
                                    {
                                        patchObj.Add(property.Id,
                                            recObj[property.Id].ToString() == "null" ? null : recObj[property.Id]);
                                    }
                                }
                                else
                                {
                                    patchObj.Add(property.Id, recObj[property.Id]);
                                }
                            }
                        }
                    }

                    return patchObj;
                }
                else
                {
                    throw new Exception($"Unable to get fields meta data for schema {schema.Id}");
                }
            }
            catch (Exception e)
            {
                Logger.Error(e, e.Message);
                throw;
            }
        }
    }
}