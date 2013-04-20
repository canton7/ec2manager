using Caliburn.Micro;
using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Caliburn.Micro;
using System.Threading;

namespace Ec2Manager
{
    [Export]
    [PartCreationPolicy(CreationPolicy.NonShared)]
    public class Logger : PropertyChangedBase
    {
        private BindableCollection<LogEntry> entries = new BindableCollection<LogEntry>();
        public BindableCollection<LogEntry> Entries
        {
            get { return this.entries; }
        }

        public Logger()
        {
            // Some users are dumb, and can't notice collection changes
            this.Entries.CollectionChanged += (o, e) =>
                this.NotifyOfPropertyChange(() => Entries);
        }

        public void Log(string message)
        {
            this.newLogEntry(message);
        }

        public void Log(string format, params string[] parameters)
        {
            this.newLogEntry(String.Format(format, parameters));
        }

        public void LogFromStream(Stream stream, IAsyncResult asynch)
        {
            using (var sr = new StreamReader(stream))
            {
                while (!asynch.IsCompleted)
                {
                    Thread.Sleep(100);
                    var result = sr.ReadToEnd();
                    if (string.IsNullOrEmpty(result))
                        continue;
                    this.newLogEntry(result.Trim());
                }
            }
        }

        private void newLogEntry(string text)
        {
            foreach (var entry in text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None))
            {
                this.Entries.Add(new LogEntry(entry));
            }
        }

        public class LogEntry
        {
            public string Message { get; private set; }
            public DateTime Time { get; private set; }

            public LogEntry (string message)
	        {
                this.Message = message;
                this.Time = DateTime.Now;
	        }
        }
    }
}
