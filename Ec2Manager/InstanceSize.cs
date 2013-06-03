using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Ec2Manager.Ec2Manager
{
    public struct InstanceSize
    {
        public string Key { get; private set; }
        public string Name { get; private set; }
        public double Cost { get; private set; }

        public InstanceSize(string name, string key) : this()
        {
            this.Key = key;
            this.Name = name;
            this.Cost = 0.0;
        }
    }
}
