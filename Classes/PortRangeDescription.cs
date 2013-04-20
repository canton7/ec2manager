using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Ec2Manager.Classes
{
    public class PortRangeDescription
    {
        public int FromPort { get; set; }
        public int ToPort { get; set; }
        public string Proto { get; set; }

        public PortRangeDescription(int fromPort, int toPort, string proto)
        {
            this.FromPort = fromPort;
            this.ToPort = toPort;
            this.Proto = proto;
        }

        public override string ToString()
        {
            return this.FromPort + "-" + this.ToPort + "/" + this.Proto;
        }
    }
}
