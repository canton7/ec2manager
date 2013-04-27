using Caliburn.Micro;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Ec2Manager
{
    public interface ILogger
    {
        BindableCollection<LogEntry> Entries { get; }
        void Log(string message);
        void Log(string format, params string[] parameters);
        void LogFromStream(Stream stream, IAsyncResult asynch);
    }

    public class LogEntry
    {
        public string Message { get; private set; }
        public DateTime Time { get; private set; }

        public LogEntry(string message)
        {
            this.Message = message;
            this.Time = DateTime.Now;
        }
    }
}
