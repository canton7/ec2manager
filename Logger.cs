using Caliburn.Micro;
using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading;

namespace Ec2Manager
{
    [Export]
    [PartCreationPolicy(CreationPolicy.NonShared)]
    public class Logger : PropertyChangedBase, ILogger
    {
        private const int maxLogEntries = 200;

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
                    var result = sr.ReadLine();

                    if (string.IsNullOrEmpty(result))
                        Thread.Sleep(500);
                    else
                        this.newLogEntry(result.Trim());
                }
            }
        }

        private void newLogEntry(string text)
        {
            foreach (var entry in text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None))
            {
                // Don't let the log grow too big
                if (this.Entries.Count > maxLogEntries)
                {
                    this.Entries.RemoveAt(0);
                }

                // Logic to display 'last message repeated n times'
                if (this.Entries.Count > 1 && entry == this.Entries[this.Entries.Count - 1].Message)
                {
                    this.Entries.Add(new LogEntry(1));
                }
                else if (this.Entries.Count > 2 && entry == this.Entries[this.Entries.Count - 2].Message && this.Entries[this.Entries.Count - 1].RepititionCount > 0)
                {
                    int prevRepitition = this.Entries[this.Entries.Count - 1].RepititionCount;
                    this.Entries.RemoveAt(this.Entries.Count - 1);
                    this.Entries.Add(new LogEntry(prevRepitition + 1));
                }
                else
                {
                    this.Entries.Add(new LogEntry(entry));
                }
            }
        }
    }
}
