using Naveego.Sdk.Plugins;

namespace PluginSalesforceSandbox.API.Utility
{
    public static partial class Utility
    {
        public static string GetDefaultQuery(Schema schema)
        {
            return $@"select fields(all) from {schema.Id} order by CreatedDate asc nulls last limit 200";
        }
    }
}