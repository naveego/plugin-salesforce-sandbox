using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Grpc.Core;
using Naveego.Sdk.Plugins;
using Newtonsoft.Json;
using PluginSalesforce.API.Read;
using PluginSalesforceSandbox.DataContracts;
using PluginSalesforceSandbox.API.Read;

using RichardSzalay.MockHttp;
using Xunit;
using Record = Naveego.Sdk.Plugins.Record;


namespace PluginSalesforceSandboxTest.Plugin
{
    public class PluginIntegrationTest
    {
        private ConnectRequest GetConnectSettings()
        {
            return new ConnectRequest
            {
                SettingsJson = {},
                OauthConfiguration = new OAuthConfiguration
                {
                    ClientId = "",
                    ClientSecret = "",
                    ConfigurationJson = "{}"
                },
                OauthStateJson = JsonConvert.SerializeObject(new OAuthState
                {
                    AuthToken = "",
                    RefreshToken = "",
                    Config = JsonConvert.SerializeObject(new OAuthConfig
                    {
                        InstanceUrl = ""
                    })
                })
            };
        }
        
        [Fact]
        public async Task ConnectSessionTest()
        {
            // setup
            Server server = new Server
            {
                Services = {Publisher.BindService(new PluginSalesforceSandbox.Plugin.Plugin())},
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
            Server server = new Server
            {
                Services = {Publisher.BindService(new PluginSalesforceSandbox.Plugin.Plugin())},
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
            Server server = new Server
            {
                Services = {Publisher.BindService(new PluginSalesforceSandbox.Plugin.Plugin())},
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
            Server server = new Server
            {
                Services = {Publisher.BindService(new PluginSalesforceSandbox.Plugin.Plugin())},
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
        public async Task DiscoverSchemasQueryRefreshTest()
        {
            // setup
            Server server = new Server
            {
                Services = {Publisher.BindService(new PluginSalesforceSandbox.Plugin.Plugin())},
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
                ToRefresh = {new Schema {Id = "Custom", Name = "Custom", Query = "SELECT id, name from Account"}}
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
        public async Task ReadStreamTest()
        {
            // setup
            Server server = new Server
            {
                Services = {Publisher.BindService(new PluginSalesforceSandbox.Plugin.Plugin())},
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
                    Id = "Policy__c",
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
            Assert.Equal(14538, records.Count);

            // cleanup
            await channel.ShutdownAsync();
            await server.ShutdownAsync();
        }
        private Schema GetTestSchema(string endpointId = null, string id = "test", string name = "test", string query = "")
        {
            return new Schema
            {
                Id = id,
                Name = name,
                Query = query
            };
        }
        [Fact]
        public async Task ReadStreamRealTimeTest()
        {
            // setup
            Server server = new Server
            {
                Services = {Publisher.BindService(new PluginSalesforceSandbox.Plugin.Plugin())},
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
            
            var schema = GetTestSchema(null, @"Lead", @"Lead");
            // var schema = GetTestSchema(null, @"LeadUpdates", @"LeadUpdates", "Select Id, Name FROM Lead");
            // var schema = GetTestSchema(null, @"/topic/LeadUpdates", @"/topic/LeadUpdates", "Select Id, Name FROM Lead");

            
            var connectRequest = GetConnectSettings();

            var schemaRequest = new DiscoverSchemasRequest
            {
                Mode = DiscoverSchemasRequest.Types.Mode.Refresh,
                ToRefresh = {schema}
            };

            var realTimeSettings = new RealTimeSettings();
            realTimeSettings.ChannelName = "AccountQA";
            
            var request = new ReadRequest()
            {
                DataVersions = new DataVersions
                {
                    JobId = "test",
                    JobDataVersion = 1
                },
                JobId = "test",
                RealTimeStateJson = JsonConvert.SerializeObject(new RealTimeState()),
                RealTimeSettingsJson = JsonConvert.SerializeObject(realTimeSettings),
            };
            
            // act
            var records = new List<Record>();
            try
            {
                client.Configure(configureRequest);
                client.Connect(connectRequest);
                var schemasResponse = client.DiscoverSchemas(schemaRequest);
                request.Schema = schemasResponse.Schemas[0];

                var cancellationToken = new CancellationTokenSource();
                cancellationToken.CancelAfter(5000);
                cancellationToken.CancelAfter(500000);
                var response = client.ReadStream(request, null, null, cancellationToken.Token);
                var responseStream = response.ResponseStream;


                while (await responseStream.MoveNext())
                {
                    records.Add(responseStream.Current);
                }
            }
            catch (Exception e)
            {
                Assert.Equal("Status(StatusCode=\"Cancelled\", Detail=\"Cancelled\")", e.Message);
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
        public async Task ReadStreamQueryTest()
        {
            // setup
            Server server = new Server
            {
                Services = {Publisher.BindService(new PluginSalesforceSandbox.Plugin.Plugin())},
                Ports = {new ServerPort("localhost", 0, ServerCredentials.Insecure)}
            };
            server.Start();

            var port = server.Ports.First().BoundPort;

            var channel = new Channel($"localhost:{port}", ChannelCredentials.Insecure);
            var client = new Publisher.PublisherClient(channel);

            var connectRequest = GetConnectSettings();
            
            var discoverRequest = new DiscoverSchemasRequest
            {
                Mode = DiscoverSchemasRequest.Types.Mode.Refresh,
                ToRefresh = {new Schema {Id = "Custom", Name = "Custom", Query = "SELECT id, name from Account"}}
            };

            // act
            client.Connect(connectRequest);
            var discoverResponse = client.DiscoverSchemas(discoverRequest);

            var request = new ReadRequest()
            {
                Schema = discoverResponse.Schemas.First(),
            };

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
            Server server = new Server
            {
                Services = {Publisher.BindService(new PluginSalesforceSandbox.Plugin.Plugin())},
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
            Server server = new Server
            {
                Services = {Publisher.BindService(new PluginSalesforceSandbox.Plugin.Plugin())},
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
            Server server = new Server
            {
                Services = {Publisher.BindService(new PluginSalesforceSandbox.Plugin.Plugin())},
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
            Server server = new Server
            {
                Services = {Publisher.BindService(new PluginSalesforceSandbox.Plugin.Plugin())},
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