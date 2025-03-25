using System.Collections.Generic;
using System.Linq;
using Naveego.Sdk.Plugins;

namespace PluginSalesforceSandbox.API.Utility
{
  public static partial class Utility
  {
    public static string GetSingleRecordsQuery(string sObjectId, List<string> recordIds)
    {
      return $"select fields(all) from {sObjectId} where id in ('{string.Join("','", recordIds)}')";
    }
  }
}