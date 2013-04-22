using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Serialization;

namespace Ec2Manager.Configuration
{
    public class Snapshot
    {
        [XmlElement]
        public string SnapshotId { get; set; }

        [XmlElement]
        public string Name { get; set; }
    }
}
