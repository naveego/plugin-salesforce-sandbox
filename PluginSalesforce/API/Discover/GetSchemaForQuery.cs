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
using PluginSalesforce.Helper;

namespace PluginSalesforce.API.Discover
{
    public static partial class Discover
    {
        private static string COLUMNS_START = "SELECT ";
        private static string COLUMNS_END = " FROM";
        private static string TABLE_START = "FROM ";
        private static string TABLE_END = " ";
        public static async Task<Schema> GetSchemaForQuery(RequestHelper client, Schema schema, uint sampleSize = 5, bool realTimeRead = false)
        {
            var query = schema.Query;

            // if (schema.Name.ToLower().StartsWith(@"/topic/"))
            // {
            //     var safeQuery = query.Replace('\n', ' ');
            //     var start = safeQuery.ToUpper().IndexOf(COLUMNS_START) + COLUMNS_START.Length;
            //     var end = safeQuery.ToUpper().IndexOf(COLUMNS_END, start);
            //     
            //     
            //     
            //     if (start == -1 || end == -1 || end - start <= 0)
            //     {
            //         throw new Exception("Invalid query syntax - could not identify columns");
            //     }
            //     var columns = safeQuery.Substring(start, end - start).Split(',');
            //    
            //     var tableNameStart = safeQuery.IndexOf(TABLE_START, end) + TABLE_START.Length;
            //     var tableNameEnd = safeQuery.ToUpper().IndexOf(TABLE_END, tableNameStart);
            //     if (tableNameEnd == -1 || tableNameEnd > safeQuery.Length)
            //     {
            //         tableNameEnd = safeQuery.Length;
            //     }
            //
            //     if (tableNameEnd - tableNameStart <= 0)
            //     {
            //         throw new Exception("Invalid query syntax - could not identify table");
            //     }
            //     
            //     var tableName = safeQuery.Substring(tableNameStart, tableNameEnd-tableNameStart);
            //     var properties = new List<Property>();
            //     foreach (var column in columns)
            //     {
            //         var safeColumn = column.Trim();
            //         var property = new Property
            //         {
            //             Id = safeColumn,
            //             Name = safeColumn,
            //             Description = "",
            //             Type = PropertyType.String,
            //             IsKey = safeColumn.ToLower() == "id",
            //             IsNullable = true,
            //             IsCreateCounter = false,
            //             IsUpdateCounter = false,
            //             PublisherMetaJson = "",
            //             TypeAtSource = ""
            //         };
            //         properties.Add(property);
            //     }
            //     
            //     schema.Properties.Clear();
            //     schema.Properties.AddRange(properties);
            //     return schema;
            // }
            
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
                        IsKey = key.ToLower() == "id",
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