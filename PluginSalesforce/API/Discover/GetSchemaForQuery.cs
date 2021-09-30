using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Web;
using Naveego.Sdk.Logging;
using Naveego.Sdk.Plugins;
using Newtonsoft.Json;
using PluginSalesforce.DataContracts;

namespace PluginSalesforce.API.Discover
{
    public static partial class Discover
    {
        public static async Task<Schema> GetSchemaForQuery(HttpClient client, Schema schema)
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

            return schema;
        }
    }
}