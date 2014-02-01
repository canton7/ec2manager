using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Ec2Manager.Configuration
{
    public struct KeyDescription
    {
        public string Key { get; private set; }
        public string Fingerprint { get; private set; }

        public KeyDescription(string key, string fingerprint)
            : this()
        {
            this.Key = key;
            this.Fingerprint = fingerprint;
        }
    }
}
