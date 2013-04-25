using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Ec2Manager.Events
{
    public class TerminateInstanceEvent
    {
        public Ec2Manager Manager { get; set; }
    }
}
