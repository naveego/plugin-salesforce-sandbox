using System.Collections.Generic;
using System.Net.Http;
using System.Web;
using Naveego.Sdk.Plugins;
using Newtonsoft.Json;
using PluginSalesforce.DataContracts;
using PluginSalesforce.Helper;

namespace PluginSalesforce.API.Read
{
    public static partial class Read
    {
        public static async IAsyncEnumerable<Dictionary<string, object>> GetRecordsForQuery(RequestHelper client, Schema schema)
        {
            var query = schema.Query;
            
            // get records
            var response = await client.GetAsync($"/query?q={HttpUtility.UrlEncode(query)}");
            response.EnsureSuccessStatusCode();
            
            var recordsResponse =
                JsonConvert.DeserializeObject<RecordsResponse>(await response.Content.ReadAsStringAsync());

            var records = recordsResponse.Records;

            foreach (var record in records)
            {
                yield return record;
            }

            while (!recordsResponse.Done)
            {
                response = await client.GetAsync(recordsResponse.NextRecordsUrl.Replace("/services/data/v52.0", ""));
                response.EnsureSuccessStatusCode();
                
                recordsResponse =
                    JsonConvert.DeserializeObject<RecordsResponse>(await response.Content.ReadAsStringAsync());
                
                foreach (var record in records)
                {
                    yield return record;
                }
            }
        }
    }
}