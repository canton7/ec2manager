using Ec2Manager.Ec2Manager;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Ec2Manager.Events
{
    public class ReconnectInstanceEvent
    {
        public Ec2Instance Instance { get; set; }
    }
}
