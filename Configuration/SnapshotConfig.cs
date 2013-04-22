using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Serialization;

namespace Ec2Manager.Configuration
{
    [XmlRoot("SnapshotConfig")]
    public class SnapshotConfig
    {
        public static SnapshotConfig FromFile(string file)
        {
            SnapshotConfig snapshotConfig;
            var xmlSerializer = new XmlSerializer(typeof(SnapshotConfig));

            using (var sr = new StreamReader(file))
            {
                snapshotConfig = (SnapshotConfig)xmlSerializer.Deserialize(sr);
            }

            return snapshotConfig;
        }

        public static SnapshotConfig FromString(string str)
        {
            SnapshotConfig snapshotConfig;
            var xmlSerializer = new XmlSerializer(typeof(SnapshotConfig));

            using (var sr = new StringReader(str))
            {
                snapshotConfig = (SnapshotConfig)xmlSerializer.Deserialize(sr);
            }

            return snapshotConfig;
        }

        public SnapshotConfig()
        {
            this.Snapshots = new Snapshot[0];
        }

        public SnapshotConfig(IEnumerable<Snapshot> snapshots)
        {
            this.Snapshots = snapshots.ToArray();
        }

        [XmlArrayItem("Snapshot")]
        public Snapshot[] Snapshots { get; set; }
    }
}
