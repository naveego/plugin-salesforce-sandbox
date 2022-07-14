using Naveego.Sdk.Plugins;

namespace PluginSalesforceSandbox.API.Utility
{
    public static partial class Utility
    {
        public static string GetDefaultQuery(Schema schema, int loopOffset = -1)
        {
            return $@"select fields(all) from {schema.Id} order by id asc nulls last limit 200 {(loopOffset > 0 ? $"offset {200*loopOffset}" : "")}";
        }
    }
}