using System.Collections.Generic;
using Newtonsoft.Json;

namespace PluginSalesforceSandbox.DataContracts
{
  public class ChangeDataEvent
  {
    [JsonProperty("ChangeEventHeader")]
    public ChangeDataEventHeader ChangeEventHeader { get; set; }
  }

  public class ChangeDataEventHeader
  {
    [JsonProperty("entityName")]
    public string EntityName { get; set; }

    [JsonProperty("recordIds")]
    public List<string> RecordIds { get; set; }

    [JsonProperty("changeType")]
    public string ChangeType { get; set; }

    [JsonProperty("changeOrigin")]
    public string ChangeOrigin { get; set; }

    [JsonProperty("transactionKey")]
    public string TransactionKey { get; set; }

    [JsonProperty("sequenceNumber")]
    public long SequenceNumber { get; set; }

    [JsonProperty("commitTimestamp")]
    public long CommitTimestamp { get; set; }

    [JsonProperty("commitNumber")]
    public long CommitNumber { get; set; }

    [JsonProperty("commitUser")]
    public string CommitUser { get; set; }

    [JsonProperty("nulledFields")]
    public List<string> NulledFields { get; set; }

    [JsonProperty("diffFields")]
    public List<string> DiffFields { get; set; }

    [JsonProperty("changedFields")]
    public List<string> ChangedFields { get; set; }
  }
}