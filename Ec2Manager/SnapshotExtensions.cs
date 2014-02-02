using Amazon.EC2.Model;
using Ec2Manager.Properties;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Ec2Manager.Ec2Manager
{
    public static class SnapshotExtensions
    {
        public static string DescriptionWithoutPrefix(this Snapshot snapshot)
        {
            return snapshot.Description.StartsWith(Settings.Default.SnapshotPrefix) ?
                snapshot.Description.Substring(Settings.Default.SnapshotPrefix.Length).TrimStart(' ') :
                snapshot.Description;
        }

        public static bool DescriptionHasPrefix(this Snapshot snapshot)
        {
            return snapshot.Description.StartsWith(Settings.Default.SnapshotPrefix);
        }
    }
}
