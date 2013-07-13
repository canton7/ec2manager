﻿using Caliburn.Micro;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Ec2Manager.Utilities
{
    public interface ILogger
    {
        void Log(string message);
        void Log(string format, params string[] parameters);
        void LogFromStream(IAsyncResult asynch, Stream stdout, Stream stderr = null, CancellationToken? cancellationToken = null);
    }

    public class LogEntry
    {
        public string Message { get; private set; }
        public DateTime Time { get; private set; }
        public int RepititionCount { get; private set; }
        public bool IsComplete { get; set; }

        public LogEntry(string message, bool isComplete = true)
        {
            this.Message = message;
            this.Time = DateTime.Now;
            this.RepititionCount = 0;
            this.IsComplete = isComplete;
        }

        public LogEntry(int repititionCount)
        {
            this.RepititionCount = repititionCount;
            this.Time = DateTime.Now;
            this.IsComplete = true;

            this.Message = string.Format("Last message repeated {0} times", this.RepititionCount);
        }

        public void AddMessagePart(string message)
        {
            this.Message += message;
        }
    }
}