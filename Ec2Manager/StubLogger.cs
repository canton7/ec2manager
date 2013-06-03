using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Ec2Manager.Ec2Manager
{
    public class StubLogger : ILogger
    {
        public void Log(string message)
        {
        }

        public void Log(string format, params string[] parameters)
        {
        }

        public void LogFromStream(IAsyncResult asynch, Stream stdout, Stream stderr = null, CancellationToken? cancellationToken = null)
        {
        }
    }
}
