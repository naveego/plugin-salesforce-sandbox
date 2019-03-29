using System;
using System.Collections.Generic;
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
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using PluginSalesforce.DataContracts;
using PluginSalesforce.Helper;
using Pub;

namespace PluginSalesforce.Plugin
{
    public class Plugin : Publisher.PublisherBase
    {
        private RequestHelper _client;
        private readonly HttpClient _injectedClient;
        private readonly ServerStatus _server;
        private TaskCompletionSource<bool> _tcs;
        private string _baseUrl;

        public Plugin(HttpClient client = null)
        {
            _injectedClient = client ?? new HttpClient();
            _server = new ServerStatus
            {
                Connected = false,
                WriteConfigured = false
            };
            _baseUrl = String.Empty;
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

            // get code from redirect url
            string code;
            var uri = new Uri(request.RedirectUrl);

            try
            {
                code = HttpUtility.UrlDecode(HttpUtility.ParseQueryString(uri.Query).Get("code"));
            }
            catch (Exception e)
            {
                Logger.Error(e.Message);
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
            
            // build json request
            var json = new StringContent(JsonConvert.SerializeObject(new TokenRequest
            {
                Code = code,
                ClientId = clientId,
                ClientSecret = clientSecret,
                GrantType = grantType,
                RedirectUri = redirectUrl
            }));

            // get tokens
            var oAuthState = new OAuthState();
            try
            {
                var client = _injectedClient;
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                
                var response = await client.PostAsync(tokenUrl, json);
                response.EnsureSuccessStatusCode();

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
                Logger.Error(e.Message);
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
        /// Establishes a connection with a Salesforce instance. Creates an authenticated http client and tests it.
        /// </summary>
        /// <param name="request"></param>
        /// <param name="context"></param>
        /// <returns>A message indicating connection success</returns>
        public override async Task<ConnectResponse> Connect(ConnectRequest request, ServerCallContext context)
        {
            _server.Connected = false;

            Logger.Info("Connecting...");
            Logger.Info("Got OAuth State: " + !String.IsNullOrEmpty(request.OauthStateJson));
            Logger.Info("Got OAuthConfig " +
                        !String.IsNullOrEmpty(JsonConvert.SerializeObject(request.OauthConfiguration)));

            OAuthState oAuthState;
            OAuthConfig oAuthConfig;
            try
            {
                oAuthState = JsonConvert.DeserializeObject<OAuthState>(request.OauthStateJson);
                oAuthConfig = JsonConvert.DeserializeObject<OAuthConfig>(oAuthState.Config);
            }
            catch (Exception e)
            {
                Logger.Error(e.Message);
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
                Logger.Error(e.Message);
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
                Logger.Error(e.Message);
                throw;
            }

            // attempt to call the Salesforce api
            try
            {
                var response = await _client.GetAsync("/tabs");
                response.EnsureSuccessStatusCode();

                _server.Connected = true;

                Logger.Info("Connected to Salesforce");
            }
            catch (Exception e)
            {
                Logger.Error(e.Message);

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
            Logger.Info("Discovering Schemas...");

            DiscoverSchemasResponse discoverSchemasResponse = new DiscoverSchemasResponse();
            List<TabObject> tabsResponse;

            // get the tabs present in Salesforce
            try
            {
                Logger.Debug("Getting tabs...");
                var response = await _client.GetAsync("/tabs");
                response.EnsureSuccessStatusCode();

                Logger.Debug(await response.Content.ReadAsStringAsync());

                tabsResponse =
                    JsonConvert.DeserializeObject<List<TabObject> >(await response.Content.ReadAsStringAsync());
            }
            catch (Exception e)
            {
                Logger.Error(e.Message);
                throw;
            }

            // attempt to get a schema for each tab found
            try
            {
                Logger.Info($"Schemas attempted: {tabsResponse.Count}");

                var tasks = tabsResponse.Select(GetSchemaForTab)
                    .ToArray();

                await Task.WhenAll(tasks);

                discoverSchemasResponse.Schemas.AddRange(tasks.Where(x => x.Result != null).Select(x => x.Result));
            }
            catch (Exception e)
            {
                Logger.Error(e.Message);
                throw;
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

            Logger.Info($"Publishing records for schema: {schema.Name}");

            try
            {
                var recordsCount = 0;
                var records = new List<Dictionary<string, object>>();

                // get all records
                // build query string
                StringBuilder query = new StringBuilder("select+");

                foreach (var property in schema.Properties)
                {
                    query.Append($"{property.Id},");
                }

                // remove trailing comma
                query.Length--;

                query.Append($"+from+{schema.Id}");
                
                // get records for schema page by page
                var response = await _client.GetAsync(String.Format("/query?q={0}", query));
                response.EnsureSuccessStatusCode();

                var recordsResponse =
                    JsonConvert.DeserializeObject<RecordsResponse>(await response.Content.ReadAsStringAsync());
                
                records.AddRange(recordsResponse.Records);

                while (!recordsResponse.Done && _server.Connected)
                {
                    response = await _client.GetAsync(recordsResponse.NextRecordsUrl);
                    response.EnsureSuccessStatusCode();

                    recordsResponse =
                        JsonConvert.DeserializeObject<RecordsResponse>(await response.Content.ReadAsStringAsync());
                    
                    records.AddRange(recordsResponse.Records);
                }
                
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
                        Logger.Error(e.Message);
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
                    await responseStream.WriteAsync(recordOutput);
                    recordsCount++;
                }
                
                Logger.Info($"Published {recordsCount} records");
            }
            catch (Exception e)
            {
                Logger.Error(e.Message);
                throw;
            }
        }

        /// <summary>
        /// Prepares the plugin to handle a write request
        /// </summary>
        /// <param name="request"></param>
        /// <param name="context"></param>
        /// <returns></returns>
        public override Task<PrepareWriteResponse> PrepareWrite(PrepareWriteRequest request, ServerCallContext context)
        {
            Logger.Info("Preparing write...");
            _server.WriteConfigured = false;

            var writeSettings = new WriteSettings
            {
                CommitSLA = request.CommitSlaSeconds,
                Schema = request.Schema
            };

            _server.WriteSettings = writeSettings;
            _server.WriteConfigured = true;

            Logger.Info("Write prepared.");
            return Task.FromResult(new PrepareWriteResponse());
        }

        /// <summary>
        /// Takes in records and writes them out to Salesforce then sends acks back to the client
        /// </summary>
        /// <param name="requestStream"></param>
        /// <param name="responseStream"></param>
        /// <param name="context"></param>
        /// <returns></returns>
//        public override async Task WriteStream(IAsyncStreamReader<Record> requestStream,
//            IServerStreamWriter<RecordAck> responseStream, ServerCallContext context)
//        {
//            try
//            {
//                Logger.Info("Writing records to Salesforce...");
//                var schema = _server.WriteSettings.Schema;
//                var sla = _server.WriteSettings.CommitSLA;
//                var inCount = 0;
//                var outCount = 0;
//
//                // get next record to publish while connected and configured
//                while (await requestStream.MoveNext(context.CancellationToken) && _server.Connected &&
//                       _server.WriteConfigured)
//                {
//                    var record = requestStream.Current;
//                    inCount++;
//
//                    Logger.Debug($"Got record: {record.DataJson}");
//
//                    // send record to source system
//                    // timeout if it takes longer than the sla
//                    Task<string> task;
//
//                    if (record.Action == Record.Types.Action.Delete)
//                    {
//                        task = Task.Run(() => DeleteRecord(schema, record));
//                    }
//                    else
//                    {
//                        task = Task.Run(() => PutRecord(schema, record));
//                    }
//
//                    if (task.Wait(TimeSpan.FromSeconds(sla)))
//                    {
//                        // send ack
//                        var ack = new RecordAck
//                        {
//                            CorrelationId = record.CorrelationId,
//                            Error = task.Result
//                        };
//                        await responseStream.WriteAsync(ack);
//
//                        if (String.IsNullOrEmpty(task.Result))
//                        {
//                            outCount++;
//                        }
//                    }
//                    else
//                    {
//                        // send timeout ack
//                        var ack = new RecordAck
//                        {
//                            CorrelationId = record.CorrelationId,
//                            Error = "timed out"
//                        };
//                        await responseStream.WriteAsync(ack);
//                    }
//                }
//
//                Logger.Info($"Wrote {outCount} of {inCount} records to Salesforce.");
//            }
//            catch (Exception e)
//            {
//                Logger.Error(e.Message);
//                throw;
//            }
//        }

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
        /// Gets a schema for a given endpoint
        /// </summary>
        /// <param name="tab"></param>
        /// <returns>returns a schema or null if unavailable</returns>
        private async Task<Schema> GetSchemaForTab(TabObject tab)
        {
            // base schema to be added to
            var schema = new Schema
            {
                Id = tab.SobjectName,
                Name = tab.Label,
                Description = tab.Name,
                PublisherMetaJson = JsonConvert.SerializeObject(new PublisherMetaJson
                {
                }),
                DataFlowDirection = Schema.Types.DataFlowDirection.ReadWrite
            };

            try
            {
                Logger.Debug($"Getting fields for: {tab.Label}");

                // get fields for module
                var response = await _client.GetAsync(String.Format("sobjects/{0}/describe", tab.SobjectName));

                // if response is not found return null
                if (response.StatusCode == HttpStatusCode.NotFound)
                {
                    Logger.Debug($"No fields for: {tab.SobjectName}");
                    return null;
                }

                Logger.Debug($"Got fields for: {tab.SobjectName}");

                // for each field in the schema add a new property
                var describeResponse =
                    JsonConvert.DeserializeObject<DescribeResponse>(await response.Content.ReadAsStringAsync());

                foreach (var field in describeResponse.Fields)
                {
                    var property = new Property
                    {
                        Id = field.Name,
                        Name = field.Label,
                        Type = GetPropertyType(field),
                        IsKey = field.IdLookup,
                        IsCreateCounter = field.Name == "CreatedDate",
                        IsUpdateCounter = field.Name == "LastModifiedDate",
                        TypeAtSource = field.Type,
                        IsNullable = field.DefaultedOnCreate
                    };

                    schema.Properties.Add(property);
                }

                Logger.Debug($"Added schema for: {tab.SobjectName}");
                return schema;
            }
            catch (Exception e)
            {
                Logger.Error(e.Message);
                return null;
            }
        }

        /// <summary>
        /// Gets the Naveego type from the provided Salesforce information
        /// </summary>
        /// <param name="field"></param>
        /// <returns>The property type</returns>
        private PropertyType GetPropertyType(FieldObject field)
        {
            switch (field.SoapType)
            {
                case "xsd:boolean":
                    return PropertyType.Bool;
                case "xsd:int":
                    return PropertyType.Integer;
                case "xsd:double":
                    return PropertyType.Float;
                case "xsd:date":
                    return PropertyType.Date;
                case "xsd:dateTime":
                    return PropertyType.Datetime;
                case "xsd:string":
                    if (field.Length >= 1024)
                    {
                        return PropertyType.Text;
                    }
                    return PropertyType.String;
                default:
                    return PropertyType.String;
            }
        }

        /// <summary>
        /// Writes a record out to Salesforce
        /// </summary>
        /// <param name="schema"></param>
        /// <param name="record"></param>
        /// <returns></returns>
//        private async Task<string> PutRecord(Schema schema, Record record)
//        {
//            Dictionary<string, object> recObj;
//
//            if (String.IsNullOrEmpty(endpoint.MetaDataPath))
//            {
//                try
//                {
//                    // check if source has newer record than write back record
//                    recObj = JsonConvert.DeserializeObject<Dictionary<string, object>>(record.DataJson);
//
//                    if (recObj.ContainsKey("id"))
//                    {
//                        if (recObj["id"] != null)
//                        {
//                            // record already exists, check date then patch it
//                            var id = recObj["id"];
//
//                            // build and send request
//                            var path = String.Format("{0}/{1}", endpoint.ReadPaths.First(), id);
//
//                            var response = await _client.GetAsync(path);
//                            response.EnsureSuccessStatusCode();
//
//                            var srcObj =
//                                JsonConvert.DeserializeObject<Dictionary<string, object>>(
//                                    await response.Content.ReadAsStringAsync());
//
//                            // get modified key from schema
//                            var modifiedKey = schema.Properties.First(x => x.IsUpdateCounter);
//
//                            if (recObj.ContainsKey(modifiedKey.Id) && srcObj.ContainsKey(modifiedKey.Id))
//                            {
//                                if (recObj[modifiedKey.Id] != null && srcObj[modifiedKey.Id] != null)
//                                {
//                                    // if source is newer than request exit
//                                    if (DateTime.Parse(recObj[modifiedKey.Id].ToString()) <=
//                                        DateTime.Parse(srcObj[modifiedKey.Id].ToString()))
//                                    {
//                                        Logger.Info($"Source is newer for record {record.DataJson}");
//                                        return "source system is newer than requested write back";
//                                    }
//                                }
//                            }
//
//                            var patchObj = GetPatchObject(endpoint, recObj);
//
//                            var content = new StringContent(JsonConvert.SerializeObject(patchObj), Encoding.UTF8,
//                                "application/json");
//
//                            response = await _client.PatchAsync(path, content);
//                            response.EnsureSuccessStatusCode();
//
//                            Logger.Info("Modified 1 record.");
//                            return "";
//                        }
//                        else
//                        {
//                            // record does not exist, create it
//                            var postObj = GetPostObject(endpoint, recObj);
//
//                            var content = new StringContent(JsonConvert.SerializeObject(postObj), Encoding.UTF8,
//                                "application/json");
//
//                            var response = await _client.PostAsync(endpoint.ReadPaths.First(), content);
//                            response.EnsureSuccessStatusCode();
//
//                            Logger.Info("Created 1 record.");
//                            return "";
//                        }
//                    }
//
//                    return "Key 'id' not found on requested record to write back.";
//                }
//                catch (Exception e)
//                {
//                    Logger.Error(e.Message);
//                    return e.Message;
//                }
//            }
//
//            // code for modifying forms would go here if needed but currently is not needed
//
//            return "Write backs are only supported for Classes.";
//        }

        /// <summary>
        /// Deletes a record from Salesforce
        /// </summary>
        /// <param name="schema"></param>
        /// <param name="record"></param>
        /// <returns></returns>
//        private async Task<string> DeleteRecord(Schema schema, Record record)
//        {
//            Dictionary<string, object> recObj;
//            var endpoint = _endpointHelper.GetEndpointForName(schema.Id);
//
//            if (String.IsNullOrEmpty(endpoint.MetaDataPath))
//            {
//                try
//                {
//                    recObj = JsonConvert.DeserializeObject<Dictionary<string, object>>(record.DataJson);
//
//                    if (recObj.ContainsKey("id"))
//                    {
//                        if (recObj["id"] != null)
//                        {
//                            // delete record
//                            // try each endpoint
//                            foreach (var path in endpoint.ReadPaths)
//                            {
//                                try
//                                {
//                                    var uri = String.Format("{0}/{1}", path, recObj["id"]);
//                                    var response = await _client.DeleteAsync(uri);
//                                    response.EnsureSuccessStatusCode();
//
//                                    Logger.Info("Deleted 1 record.");
//                                    return "";
//                                }
//                                catch (Exception e)
//                                {
//                                    Logger.Error(e.Message);
//                                }
//                            }
//                        }
//
//                        return "Could not delete record with no id.";
//                    }
//
//                    return "Key 'id' not found on requested record to write back.";
//                }
//                catch (Exception e)
//                {
//                    Logger.Error(e.Message);
//                    return e.Message;
//                }
//            }
//
//            // code for modifying forms would go here if needed but currently is not needed
//
//            return "Write backs are only supported for Classes.";
//        }
    }
}