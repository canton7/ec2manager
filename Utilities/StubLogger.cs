﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Ec2Manager.Utilities
{
    public class StubLogger : ILogger
    {
        public void Log(string message)
        {
        }

        public void Log(string format, params string[] parameters)
        {
        }

        public Task LogFromStream(IAsyncResult asynch, Stream stdout, Stream stderr = null, CancellationToken? cancellationToken = null)
        {
            return Task.FromResult(0);
        }
    }
}
