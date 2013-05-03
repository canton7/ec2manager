using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Ec2Manager.Events
{
    public class CreateInstanceEvent
    {
        public string InstanceAmi { get; set; }
        public string InstanceSize { get; set; }
        public Ec2Manager Manager { get; set; }
        public string LoginAs { get; set; }
        public string AvailabilityZone { get; set; }
    }
}
