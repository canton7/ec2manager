using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Ec2Manager.Configuration
{
    public class VolumeType
    {
        public Friend Owner { get; set; }
        public string SnapshotId { get; set; }
        public string Name { get; set; }
        public bool IsCustom { get; set; }

        public VolumeType()
        {
        }

        public VolumeType(string snapshotId, string name, Friend owner)
        {
            this.SnapshotId = snapshotId;
            this.Name = name;
            this.Owner = owner;
            this.IsCustom = false;
        }

        public static VolumeType Custom(string name, Friend owner)
        {
            return new VolumeType()
            {
                Name = name,
                Owner = owner,
                IsCustom = true,
            };
        }
    }
}
