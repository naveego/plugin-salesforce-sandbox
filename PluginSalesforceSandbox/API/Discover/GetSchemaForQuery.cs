using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Web;
using Naveego.Sdk.Logging;
using Naveego.Sdk.Plugins;
using Newtonsoft.Json;
using PluginSalesforceSandbox.DataContracts;
using PluginSalesforceSandbox.Helper;

namespace PluginSalesforceSandbox.API.Discover
{
    public static partial class Discover
    {
        public static async Task<Schema> GetSchemaForQuery(RequestHelper client, Schema schema, uint sampleSize = 5)
        {
            var query = schema.Query;
            
            // get records
            var response = await client.GetAsync($"/query?q={HttpUtility.UrlEncode(query)}");
            response.EnsureSuccessStatusCode();
            
            var recordsResponse =
                JsonConvert.DeserializeObject<RecordsResponse>(await response.Content.ReadAsStringAsync());

            var records = recordsResponse.Records;

            if (recordsResponse.TotalSize == 0)
            {
                return schema;
            }
            
            try
            {
                var record = records.First();
                record.Remove("attributes");

                var properties = new List<Property>();
                foreach (var key in record.Keys)
                {
                    var property = new Property
                    {
                        Id = key,
                        Name = key,
                        Description = "",
                        Type = PropertyType.String,
                        IsKey = false,
                        IsNullable = true,
                        IsCreateCounter = false,
                        IsUpdateCounter = false,
                        PublisherMetaJson = "",
                        TypeAtSource = ""
                    };
                    properties.Add(property);
                }
                
                schema.Properties.Clear();
                schema.Properties.AddRange(properties);
            }
            catch (Exception e)
            {
                Logger.Error(e, e.Message);
                return schema;
            }

            var sampleRecords = new List<Record>();
            foreach (var record in records.Take(Convert.ToInt32(sampleSize)))
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
                sampleRecords.Add(recordOutput);
            }
            
            schema.Sample.AddRange(sampleRecords);

            return schema;
        }
    }
}