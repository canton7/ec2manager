using Ec2Manager.Classes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Ec2Manager
{
    public interface IMachineInteractionProvider
    {
        Task MountAndSetupDeviceAsync(string device, string mountPoint, Logger logger);
        IEnumerable<PortRangeDescription> GetPortDescriptions(string mountPointDir, Logger logger);
    }
}
