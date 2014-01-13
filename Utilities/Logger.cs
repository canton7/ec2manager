using Caliburn.Micro;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading;
using Ec2Manager.Classes;

namespace Ec2Manager.Utilities
{
    public class Logger : PropertyChangedBase, ILogger
    {
        private const int maxLogEntries = 200;

        private BindableCollection<LogEntry> entries = new BindableCollection<LogEntry>();
        public BindableCollection<LogEntry> Entries
        {
            get { return this.entries; }
        }

        private TaskFactory accessTaskFactory;

        public Logger()
        {
            // Some users are dumb, and can't notice collection changes
            this.Entries.CollectionChanged += (o, e) =>
                this.NotifyOfPropertyChange(() => Entries);

            this.accessTaskFactory = new TaskFactory(new SingleAccessTaskScheduler());
        }

        public void Log(string message)
        {
            this.newLogEntry(message.TrimEnd());
        }

        public void Log(string format, params string[] parameters)
        {
            this.newLogEntry(String.Format(format, parameters).TrimEnd());
        }

        public Task LogFromStream(IAsyncResult asynch, Stream stdout, Stream stderr = null, CancellationToken? cancellationToken = null)
        {
            CancellationToken token = cancellationToken.HasValue ? cancellationToken.Value : new CancellationToken();

            return Task.WhenAll(this.logFromStream(stdout, token), this.logFromStream(stderr, token));
        }
        
        private async Task logFromStream(Stream stream, CancellationToken token)
        {
            char[] buffer = new char[128];

            using (var sr = new StreamReader(stream))
            {
                while (true)
                {
                    token.ThrowIfCancellationRequested();

                    var outBytesRead = await sr.ReadAsync(buffer, 0, buffer.Length);

                    if (outBytesRead == 0)
                    {
                        if (sr.EndOfStream)
                            break;
                        else
                            await Task.Delay(100);
                    }
                    else
                    {
                        var outText = new string(buffer).TrimEnd('\0');

                        if (!string.IsNullOrEmpty(outText))
                            this.newLogEntry(outText, true, true);
                    }
                }
            }
        }

        private void newLogEntry(string text, bool allowRepititionMessages = false, bool allowIncompleteMessages = false)
        {
            this.accessTaskFactory.StartNew(() =>
                {
                    var lastMessageIsComplete = text.EndsWith("\n") || text.EndsWith("\r\n");

                    var entries = text.TrimEnd(new[]{ '\r', '\n' }).Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
                    for (int i = 0; i < entries.Length; i++)
                    {
                        var entry = entries[i];
                        var isCompleteMessage = !allowIncompleteMessages || lastMessageIsComplete || i < entries.Length - 1;
                        var allowRepititionMessage = allowRepititionMessages && !string.IsNullOrWhiteSpace(entry);

                        // Don't let the log grow too big
                        if (this.Entries.Count > maxLogEntries)
                        {
                            this.Entries.RemoveAt(0);
                        }

                        entry = entry.TrimEnd(new[] { '\r', '\n' });

                        // Logic to display 'last message repeated n times'
                        if (allowRepititionMessage && this.Entries.Count > 1 && entry == this.Entries[this.Entries.Count - 1].Message)
                        {
                            this.Entries.Add(new LogEntry(1));
                        }
                        else if (allowRepititionMessage && this.Entries.Count > 2 && entry == this.Entries[this.Entries.Count - 2].Message && this.Entries[this.Entries.Count - 1].RepititionCount > 0)
                        {
                            int prevRepitition = this.Entries[this.Entries.Count - 1].RepititionCount;
                            this.Entries.RemoveAt(this.Entries.Count - 1);
                            this.Entries.Add(new LogEntry(prevRepitition + 1));
                        }
                        else if (allowIncompleteMessages && this.Entries.Count > 0 && !this.Entries[this.Entries.Count - 1].IsComplete)
                        {
                            var oldEntry = this.Entries[this.Entries.Count - 1];
                            oldEntry.AddMessagePart(entry);
                            oldEntry.IsComplete = isCompleteMessage;
                            this.Entries.Refresh();
                        }
                        else
                        {
                            this.Entries.Add(new LogEntry(entry, isCompleteMessage));
                        }
                    }
                });
        }
    }
}
