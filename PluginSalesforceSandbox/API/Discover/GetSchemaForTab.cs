using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Naveego.Sdk.Logging;
using Naveego.Sdk.Plugins;
using Newtonsoft.Json;
using PluginSalesforceSandbox.DataContracts;
using PluginSalesforceSandbox.Helper;

namespace PluginSalesforceSandbox.API.Discover
{
    public static partial class Discover
    {
        /// <summary>
        /// Gets a schema for a given endpoint
        /// </summary>
        /// <param name="client"></param>
        /// <param name="fieldObjectsDictionary"></param>
        /// <param name="tab"></param>
        /// <returns>returns a schema or null if unavailable</returns>
        public static async Task<Schema> GetSchemaForTab(RequestHelper client,
            ConcurrentDictionary<string, List<FieldObject>> fieldObjectsDictionary, TabObject tab)
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
                var response = await client.GetAsync(String.Format("/sobjects/{0}/describe", tab.SobjectName));

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

                fieldObjectsDictionary.TryAdd(schema.Id, describeResponse.Fields);

                foreach (var field in describeResponse.Fields)
                {
                    var property = new Property
                    {
                        Id = field.Name,
                        Name = field.Label,
                        Type = GetPropertyType(field),
                        IsKey = field.Name.ToLower() == "id",
                        IsCreateCounter = field.Name == "CreatedDate",
                        IsUpdateCounter = field.Name == "LastModifiedDate",
                        TypeAtSource = field.Type,
                        IsNullable = field.Nillable
                    };

                    schema.Properties.Add(property);
                }

                Logger.Debug($"Added schema for: {tab.SobjectName}");
                return schema;
            }
            catch (Exception e)
            {
                Logger.Error(e, e.Message);
                return null;
            }
        }
    }
}