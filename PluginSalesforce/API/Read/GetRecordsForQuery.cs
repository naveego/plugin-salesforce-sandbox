using System.Collections.Generic;
using System.Linq;
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
        public static async IAsyncEnumerable<Dictionary<string, object>> GetRecordsForDefaultQuery(RequestHelper client, Schema schema)
        {
            // get records
            List<string> allRecordIds = new List<string>();
            var loopCount = 0;
            var lastId = "";
            var previousLastId = "";
            var recordCount = 0;

            var query = Utility.Utility.GetDefaultQuery(schema);
                
            do
            {
                loopCount++;
                previousLastId = allRecordIds.LastOrDefault();

                // get records
                var records = GetRecordsForQuery(client, query);

                await foreach (var record in records)
                {
                    if (!allRecordIds.Contains(record["Id"]))
                    {
                        allRecordIds.Add(record["Id"]?.ToString());
                        recordCount++;
                        yield return record;
                    }
                }

                // update query
                lastId = allRecordIds.LastOrDefault();
                query = Utility.Utility.GetDefaultQuery(schema, loopCount);
            } while (lastId != previousLastId && recordCount % 200 == 0);
        }
        
        public static async IAsyncEnumerable<Dictionary<string, object>> GetRecordsForQuery(RequestHelper client, string query)
        {
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

                records = recordsResponse.Records;
                
                foreach (var record in records)
                {
                    yield return record;
                }
            }
        }
    }
}