using Naveego.Sdk.Plugins;
using PluginSalesforceSandbox.DataContracts;

namespace PluginSalesforceSandbox.API.Discover
{
    public static partial class Discover
    {
        /// <summary>
        /// Gets the Naveego type from the provided Salesforce information
        /// </summary>
        /// <param name="field"></param>
        /// <returns>The property type</returns>
        public static PropertyType GetPropertyType(FieldObject field)
        {
            switch (field.SoapType)
            {
                case "xsd:boolean":
                    return PropertyType.Bool;
                case "xsd:int":
                    return PropertyType.Integer;
                case "xsd:double":
                    return PropertyType.Float;
                case "xsd:date":
                    return PropertyType.Date;
                case "xsd:dateTime":
                    return PropertyType.Datetime;
                case "xsd:string":
                    if (field.Length >= 1024)
                    {
                        return PropertyType.Text;
                    }

                    return PropertyType.String;
                default:
                    if (field.SoapType.Contains("urn"))
                    {
                        return PropertyType.Json;
                    }

                    return PropertyType.String;
            }
        }
    }
}