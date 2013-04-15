using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Ec2Manager
{
    interface IMachineInteractionProvider
    {
        void MountDevice(string device, string mountPoint);
    }
}
