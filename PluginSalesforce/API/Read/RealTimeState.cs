using System;
using System.Collections.Generic;

namespace PluginSalesforce.API.Read
{
    public class RealTimeState
    {
        public long JobVersion { get; set; } = -1;
        public long ShapeVersion { get; set; } = -1;
        public DateTime LastReadTime { get; set; } = DateTime.MinValue;
    }
}