using Ec2Manager.Classes;
using Ec2Manager.Utilities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Ec2Manager
{
    public interface IMachineInteractionProvider
    {
        Task MountDeviceAsync(string device, string mountPointDir, ILogger logger, CancellationToken? cancellationToken = null);
        Task SetupDeviceAsync(string device, string mountPointDir, ILogger logger);
        Task<IEnumerable<PortRangeDescription>> GetPortDescriptionsAsync(string mountPointDir, ILogger logger, CancellationToken? cancellationToken = null);
        Task SetupFilesystemAsync(string device, ILogger logger);
    }
}
