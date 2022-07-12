using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Grpc.Core;
using LiteDB;
using Naveego.Sdk.Plugins;
using Newtonsoft.Json;
using PluginSalesforceSandbox.DataContracts;
using PluginSalesforceSandbox.API.Read;

using RichardSzalay.MockHttp;
using Xunit;
using Record = Naveego.Sdk.Plugins.Record;


namespace PluginSalesforceSandboxTest.Plugin
{
    public class PluginTest
    {
        private ConnectRequest GetConnectSettings()
        {
            return new ConnectRequest
            {
                SettingsJson = "",
                OauthConfiguration = new OAuthConfiguration
                {
                    ClientId = "client",
                    ClientSecret = "secret",
                    ConfigurationJson = "{}"
                },
                OauthStateJson =
                    "{\"RefreshToken\":\"refresh\",\"AuthToken\":\"\",\"Config\":\"{\\\"InstanceUrl\\\":\\\"https://test.salesforce.com\\\"}\"}"
            };
        }

        private MockHttpMessageHandler GetMockHttpMessageHandler()
        {
            var mockHttpHelper = new MockHttpHelper();
            var mockHttp = new MockHttpMessageHandler();

            mockHttp.When("https://test.salesforce.com/services/oauth2/token")
                .Respond("application/json", mockHttpHelper.Token);

            mockHttp.When("https://test.salesforce.com/services/data/v52.0/tabs")
                .Respond("application/json", mockHttpHelper.Tabs);

            mockHttp.When("https://test.salesforce.com/services/data/v52.0/sobjects/Lead/describe")
                .Respond("application/json", mockHttpHelper.LeadDescribe);

            mockHttp.When("https://test.salesforce.com/services/data/v52.0/sobjects/Account/describe")
                .Respond("application/json", mockHttpHelper.AccountDescribe);

            mockHttp.When("https://test.salesforce.com/services/data/v52.0/query?q=select+Id,Name,LastModifiedDate+from+Account")
                .Respond("application/json", mockHttpHelper.AccountQuery);

            mockHttp.When("https://test.salesforce.com/services/data/v52.0/sobjects/Account/1")
                .Respond("application/json", mockHttpHelper.AccountById);

            return mockHttp;
        }

        [Fact]
        public async Task BeginOAuthFlowTest()
        {
            // setup
            var mockHttp = GetMockHttpMessageHandler();

            Server server = new Server
            {
                Services = {Publisher.BindService(new PluginSalesforceSandbox.Plugin.Plugin(mockHttp.ToHttpClient()))},
                Ports = {new ServerPort("localhost", 0, ServerCredentials.Insecure)}
            };
            server.Start();

            var port = server.Ports.First().BoundPort;

            var channel = new Channel($"localhost:{port}", ChannelCredentials.Insecure);
            var client = new Publisher.PublisherClient(channel);

            var request = new BeginOAuthFlowRequest()
            {
                Configuration = new OAuthConfiguration
                {
                    ClientId = "client",
                    ClientSecret = "secret",
                    ConfigurationJson = "{}"
                },
                RedirectUrl = "http://test.com"
            };

            var clientId = request.Configuration.ClientId;
            var responseType = "code";
            var redirectUrl = request.RedirectUrl;
            var prompt = "consent";
            var display = "popup";

            var authUrl = String.Format(
                "https://test.salesforce.com/services/oauth2/authorize?client_id={0}&response_type={1}&redirect_uri={2}&prompt={3}&display={4}",
                clientId,
                responseType,
                redirectUrl,
                prompt,
                display);

            // act
            var response = client.BeginOAuthFlow(request);

            // assert
            Assert.IsType<BeginOAuthFlowResponse>(response);
            Assert.Equal(authUrl, response.AuthorizationUrl);

            // cleanup
            await channel.ShutdownAsync();
            await server.ShutdownAsync();
        }

        [Fact]
        public async Task CompleteOAuthFlowTest()
        {
            // setup
            var mockHttp = GetMockHttpMessageHandler();

            Server server = new Server
            {
                Services = {Publisher.BindService(new PluginSalesforceSandbox.Plugin.Plugin(mockHttp.ToHttpClient()))},
                Ports = {new ServerPort("localhost", 0, ServerCredentials.Insecure)}
            };
            server.Start();

            var port = server.Ports.First().BoundPort;

            var channel = new Channel($"localhost:{port}", ChannelCredentials.Insecure);
            var client = new Publisher.PublisherClient(channel);

            var completeRequest = new CompleteOAuthFlowRequest
            {
                Configuration = new OAuthConfiguration
                {
                    ClientId = "client",
                    ClientSecret = "secret",
                    ConfigurationJson = "{}"
                },
                RedirectUrl = "http://test.com?code=authcode",
                RedirectBody = ""
            };

            // act
            var response = client.CompleteOAuthFlow(completeRequest);

            // assert
            Assert.IsType<CompleteOAuthFlowResponse>(response);
            Assert.Contains("mocktoken", response.OauthStateJson);
            Assert.Contains("mocktoken", response.OauthStateJson);

            // cleanup
            await channel.ShutdownAsync();
            await server.ShutdownAsync();
        }

        [Fact]
        public async Task ConnectSessionTest()
        {
            // setup
            var mockHttp = GetMockHttpMessageHandler();

            Server server = new Server
            {
                Services = {Publisher.BindService(new PluginSalesforceSandbox.Plugin.Plugin(mockHttp.ToHttpClient()))},
                Ports = {new ServerPort("localhost", 0, ServerCredentials.Insecure)}
            };
            server.Start();

            var port = server.Ports.First().BoundPort;

            var channel = new Channel($"localhost:{port}", ChannelCredentials.Insecure);
            var client = new Publisher.PublisherClient(channel);

            var request = GetConnectSettings();
            var disconnectRequest = new DisconnectRequest();

            // act
            var response = client.ConnectSession(request);
            var responseStream = response.ResponseStream;
            var records = new List<ConnectResponse>();

            while (await responseStream.MoveNext())
            {
                records.Add(responseStream.Current);
                client.Disconnect(disconnectRequest);
            }

            // assert
            Assert.Single(records);

            // cleanup
            await channel.ShutdownAsync();
            await server.ShutdownAsync();
        }

        [Fact]
        public async Task ConnectTest()
        {
            // setup
            var mockHttp = GetMockHttpMessageHandler();

            Server server = new Server
            {
                Services = {Publisher.BindService(new PluginSalesforceSandbox.Plugin.Plugin(mockHttp.ToHttpClient()))},
                Ports = {new ServerPort("localhost", 0, ServerCredentials.Insecure)}
            };
            server.Start();

            var port = server.Ports.First().BoundPort;

            var channel = new Channel($"localhost:{port}", ChannelCredentials.Insecure);
            var client = new Publisher.PublisherClient(channel);

            var request = GetConnectSettings();

            // act
            var response = client.Connect(request);

            // assert
            Assert.IsType<ConnectResponse>(response);

            // cleanup
            await channel.ShutdownAsync();
            await server.ShutdownAsync();
        }

        [Fact]
        public async Task DiscoverSchemasAllTest()
        {
            // setup
            var mockHttp = GetMockHttpMessageHandler();

            Server server = new Server
            {
                Services = {Publisher.BindService(new PluginSalesforceSandbox.Plugin.Plugin(mockHttp.ToHttpClient()))},
                Ports = {new ServerPort("localhost", 0, ServerCredentials.Insecure)}
            };
            server.Start();

            var port = server.Ports.First().BoundPort;

            var channel = new Channel($"localhost:{port}", ChannelCredentials.Insecure);
            var client = new Publisher.PublisherClient(channel);
            
            var configureRequest = new ConfigureRequest
            {
                TemporaryDirectory = "../../../Temp",
                PermanentDirectory = "../../../Perm",
                LogDirectory = "../../../Logs",
                DataVersions = new DataVersions(),
                LogLevel = LogLevel.Debug
            };

            var connectRequest = GetConnectSettings();

            var request = new DiscoverSchemasRequest
            {
                Mode = DiscoverSchemasRequest.Types.Mode.All,
            };

            // act
            client.Configure(configureRequest);
            client.Connect(connectRequest);
            var response = client.DiscoverSchemas(request);

            // assert
            Assert.IsType<DiscoverSchemasResponse>(response);
            Assert.Equal(2, response.Schemas.Count);

            // cleanup
            await channel.ShutdownAsync();
            await server.ShutdownAsync();
        }

        [Fact]
        public async Task DiscoverSchemasRefreshTest()
        {
            // setup
            var mockHttp = GetMockHttpMessageHandler();

            Server server = new Server
            {
                Services = {Publisher.BindService(new PluginSalesforceSandbox.Plugin.Plugin(mockHttp.ToHttpClient()))},
                Ports = {new ServerPort("localhost", 0, ServerCredentials.Insecure)}
            };
            server.Start();

            var port = server.Ports.First().BoundPort;

            var channel = new Channel($"localhost:{port}", ChannelCredentials.Insecure);
            var client = new Publisher.PublisherClient(channel);

            var connectRequest = GetConnectSettings();

            var request = new DiscoverSchemasRequest
            {
                Mode = DiscoverSchemasRequest.Types.Mode.Refresh,
                ToRefresh = {new Schema {Id = "Account"}}
            };

            // act
            client.Connect(connectRequest);
            var response = client.DiscoverSchemas(request);

            // assert
            Assert.IsType<DiscoverSchemasResponse>(response);
            Assert.Single(response.Schemas);

            // cleanup
            await channel.ShutdownAsync();
            await server.ShutdownAsync();
        }
[Fact]
        public async Task ReadStreamRealTimeTest()
        {
            // setup
            Server server = new Server
            {
                Services = {Publisher.BindService(new PluginSalesforce.Plugin.Plugin())},
                Ports = {new ServerPort("localhost", 0, ServerCredentials.Insecure)}
            };
            server.Start();

            var port = server.Ports.First().BoundPort;

            var channel = new Channel($"localhost:{port}", ChannelCredentials.Insecure);
            var client = new Publisher.PublisherClient(channel);

            var schema = new Schema();
            schema.Query = "SELECT Id, Name from Lead";

            var connectRequest = GetConnectSettings();

            var schemaRequest = new DiscoverSchemasRequest
            {
                Mode = DiscoverSchemasRequest.Types.Mode.Refresh,
                ToRefresh = {schema}
            };

            var request = new ReadRequest()
            {
                DataVersions = new DataVersions
                {
                    JobId = "test",
                    JobDataVersion = 1
                },
                JobId = "test",
                RealTimeStateJson = JsonConvert.SerializeObject(new RealTimeState()),
                RealTimeSettingsJson = JsonConvert.SerializeObject(new RealTimeSettings()),
            };

            // act
            var records = new List<Record>();
            try
            {
                client.Connect(connectRequest);
                var schemasResponse = client.DiscoverSchemas(schemaRequest);
                request.Schema = schemasResponse.Schemas[0];

                var cancellationToken = new CancellationTokenSource();
                cancellationToken.CancelAfter(5000);
                var response = client.ReadStream(request, null, null, cancellationToken.Token);
                var responseStream = response.ResponseStream;


                while (await responseStream.MoveNext())
                {
                    records.Add(responseStream.Current);
                }
            }
            catch (Exception e)
            {
                Assert.Equal("Status(StatusCode=Cancelled, Detail=\"Cancelled\")", e.Message);
            }


            // assert
            Assert.Equal(3, records.Count);

            var record = JsonConvert.DeserializeObject<Dictionary<string, object>>(records[0].DataJson);
            // Assert.Equal("~", record["tilde"]);

            // cleanup
            await channel.ShutdownAsync();
            await server.ShutdownAsync();
        }
        [Fact]
        public async Task ReadStreamTest()
        {
            // setup
            var mockHttp = GetMockHttpMessageHandler();

            Server server = new Server
            {
                Services = {Publisher.BindService(new PluginSalesforceSandbox.Plugin.Plugin(mockHttp.ToHttpClient()))},
                Ports = {new ServerPort("localhost", 0, ServerCredentials.Insecure)}
            };
            server.Start();

            var port = server.Ports.First().BoundPort;

            var channel = new Channel($"localhost:{port}", ChannelCredentials.Insecure);
            var client = new Publisher.PublisherClient(channel);

            var connectRequest = GetConnectSettings();

            var request = new ReadRequest()
            {
                Schema = new Schema
                {
                    Id = "Account",
                    Properties =
                    {
                        new Property
                        {
                            Id = "Id",
                            Type = PropertyType.String,
                            IsKey = true,
                            PublisherMetaJson = JsonConvert.SerializeObject(new FieldObject
                            {
                                Updateable = false
                            })
                        },
                        new Property
                        {
                            Id = "Name",
                            Type = PropertyType.String,
                            PublisherMetaJson = JsonConvert.SerializeObject(new FieldObject
                            {
                                Updateable = true
                            })
                        },
                        new Property
                        {
                            Id = "LastModifiedDate",
                            Type = PropertyType.Datetime,
                            IsUpdateCounter = true,
                            PublisherMetaJson = JsonConvert.SerializeObject(new FieldObject
                            {
                                Updateable = false
                            })
                        }
                    }
                }
            };

            // act
            client.Connect(connectRequest);
            var response = client.ReadStream(request);
            var responseStream = response.ResponseStream;
            var records = new List<Record>();

            while (await responseStream.MoveNext())
            {
                records.Add(responseStream.Current);
            }

            // assert
            Assert.Equal(12, records.Count);

            // cleanup
            await channel.ShutdownAsync();
            await server.ShutdownAsync();
        }

        [Fact]
        public async Task ReadStreamLimitTest()
        {
            // setup
            var mockHttp = GetMockHttpMessageHandler();

            Server server = new Server
            {
                Services = {Publisher.BindService(new PluginSalesforceSandbox.Plugin.Plugin(mockHttp.ToHttpClient()))},
                Ports = {new ServerPort("localhost", 0, ServerCredentials.Insecure)}
            };
            server.Start();

            var port = server.Ports.First().BoundPort;

            var channel = new Channel($"localhost:{port}", ChannelCredentials.Insecure);
            var client = new Publisher.PublisherClient(channel);

            var connectRequest = GetConnectSettings();

            var request = new ReadRequest()
            {
                Schema = new Schema
                {
                    Id = "Account",
                    Properties =
                    {
                        new Property
                        {
                            Id = "Id",
                            Type = PropertyType.String,
                            IsKey = true
                        },
                        new Property
                        {
                            Id = "Name",
                            Type = PropertyType.String
                        },
                        new Property
                        {
                            Id = "LastModifiedDate",
                            Type = PropertyType.Datetime,
                            IsUpdateCounter = true
                        }
                    }
                },
                Limit = 1
            };

            // act
            client.Connect(connectRequest);
            var response = client.ReadStream(request);
            var responseStream = response.ResponseStream;
            var records = new List<Record>();

            while (await responseStream.MoveNext())
            {
                records.Add(responseStream.Current);
            }

            // assert
            Assert.Single(records);

            // cleanup
            await channel.ShutdownAsync();
            await server.ShutdownAsync();
        }

        [Fact]
        public async Task PrepareWriteTest()
        {
            // setup
            var mockHttp = GetMockHttpMessageHandler();

            Server server = new Server
            {
                Services = {Publisher.BindService(new PluginSalesforceSandbox.Plugin.Plugin(mockHttp.ToHttpClient()))},
                Ports = {new ServerPort("localhost", 0, ServerCredentials.Insecure)}
            };
            server.Start();

            var port = server.Ports.First().BoundPort;

            var channel = new Channel($"localhost:{port}", ChannelCredentials.Insecure);
            var client = new Publisher.PublisherClient(channel);

            var connectRequest = GetConnectSettings();

            var request = new PrepareWriteRequest()
            {
                Schema = new Schema
                {
                    Id = "Account",
                    Properties =
                    {
                        new Property
                        {
                            Id = "Id",
                            Type = PropertyType.String,
                            IsKey = true
                        },
                        new Property
                        {
                            Id = "Name",
                            Type = PropertyType.String
                        },
                        new Property
                        {
                            Id = "LastModifiedDate",
                            Type = PropertyType.Datetime,
                            IsUpdateCounter = true
                        }
                    }
                },
                CommitSlaSeconds = 1
            };

            // act
            client.Connect(connectRequest);
            var response = client.PrepareWrite(request);

            // assert
            Assert.IsType<PrepareWriteResponse>(response);

            // cleanup
            await channel.ShutdownAsync();
            await server.ShutdownAsync();
        }

        [Fact]
        public async Task WriteStreamTest()
        {
            // setup
            var mockHttp = GetMockHttpMessageHandler();

            Server server = new Server
            {
                Services = {Publisher.BindService(new PluginSalesforceSandbox.Plugin.Plugin(mockHttp.ToHttpClient()))},
                Ports = {new ServerPort("localhost", 0, ServerCredentials.Insecure)}
            };
            server.Start();

            var port = server.Ports.First().BoundPort;

            var channel = new Channel($"localhost:{port}", ChannelCredentials.Insecure);
            var client = new Publisher.PublisherClient(channel);

            var connectRequest = GetConnectSettings();
            
            var discoverSchemasRequest = new DiscoverSchemasRequest
            {
                Mode = DiscoverSchemasRequest.Types.Mode.Refresh,
                ToRefresh = {new Schema {Id = "Account"}}
            };

            var prepareRequest = new PrepareWriteRequest()
            {
                Schema = new Schema
                {
                    Id = "Account",
                    Properties =
                    {
                        new Property
                        {
                            Id = "Id",
                            Type = PropertyType.String,
                            IsKey = true
                        },
                        new Property
                        {
                            Id = "Name",
                            Type = PropertyType.String
                        },
                        new Property
                        {
                            Id = "LastModifiedDate",
                            Type = PropertyType.Datetime,
                            IsUpdateCounter = true
                        }
                    }
                },
                CommitSlaSeconds = 5
            };

            var records = new List<Record>()
            {
                {
                    new Record
                    {
                        Action = Record.Types.Action.Upsert,
                        CorrelationId = "test",
                        DataJson = "{\"Id\":1,\"Name\":\"Test Company\",\"LastModifiedDate\":\"4/10/2019\"}"
                    }
                }
            };

            var recordAcks = new List<RecordAck>();

            // act
            client.Connect(connectRequest);
            client.DiscoverSchemas(discoverSchemasRequest);
            client.PrepareWrite(prepareRequest);

            using (var call = client.WriteStream())
            {
                var responseReaderTask = Task.Run(async () =>
                {
                    while (await call.ResponseStream.MoveNext())
                    {
                        var ack = call.ResponseStream.Current;
                        recordAcks.Add(ack);
                    }
                });

                foreach (Record record in records)
                {
                    await call.RequestStream.WriteAsync(record);
                }

                await call.RequestStream.CompleteAsync();
                await responseReaderTask;
            }

            // assert
            Assert.Single(recordAcks);
            Assert.Equal("", recordAcks[0].Error);
            Assert.Equal("test", recordAcks[0].CorrelationId);

            // cleanup
            await channel.ShutdownAsync();
            await server.ShutdownAsync();
        }

        [Fact]
        public async Task DisconnectTest()
        {
            // setup
            var mockHttp = GetMockHttpMessageHandler();

            Server server = new Server
            {
                Services = {Publisher.BindService(new PluginSalesforceSandbox.Plugin.Plugin(mockHttp.ToHttpClient()))},
                Ports = {new ServerPort("localhost", 0, ServerCredentials.Insecure)}
            };
            server.Start();

            var port = server.Ports.First().BoundPort;

            var channel = new Channel($"localhost:{port}", ChannelCredentials.Insecure);
            var client = new Publisher.PublisherClient(channel);

            var request = new DisconnectRequest();

            // act
            var response = client.Disconnect(request);

            // assert
            Assert.IsType<DisconnectResponse>(response);

            // cleanup
            await channel.ShutdownAsync();
            await server.ShutdownAsync();
        }
    }
}