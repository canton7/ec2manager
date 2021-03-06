﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading;
using Ec2Manager.Classes;
using Stylet;

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

            // If we let it be posted asynchronously, the converter starts iterating it while we're still modifying it
            this.Entries.PropertyChangedDispatcher = a => a();

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
            Func<bool> isComplete = () => asynch.IsCompleted;
            return Task.WhenAll(this.logFromStream(stdout, token, "stdout", isComplete), this.logFromStream(stderr, token, "stderr", isComplete));
        }
        
        private async Task logFromStream(Stream stream, CancellationToken token, string category, Func<bool> isComplete)
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
                        if (isComplete())
                            break;
                        else
                            await Task.Delay(100);
                    }
                    else
                    {
                        var outText = new string(buffer, 0, outBytesRead);

                        if (!string.IsNullOrEmpty(outText))
                            this.newLogEntry(outText, category, true, true);
                    }
                }
            }
        }

        private void newLogEntry(string text, string category = null, bool allowRepititionMessages = false, bool allowIncompleteMessages = false)
        {
            this.accessTaskFactory.StartNew(() =>
                {
                    var lastMessageIsComplete = text.EndsWith("\n") || text.EndsWith("\r\n");

                    var entries = text.TrimEnd(new[]{ '\r', '\n' }).Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
                    for (int i = 0; i < entries.Length; i++)
                    {
                        var entriesInCategory = this.entries.Reverse().Where(x => x.Category == category);

                        var entry = entries[i];
                        var isCompleteMessage = !allowIncompleteMessages || lastMessageIsComplete || i < entries.Length - 1;
                        var allowRepititionMessage = allowRepititionMessages && !string.IsNullOrWhiteSpace(entry);

                        // Don't let the log grow too big
                        if (this.Entries.Count > maxLogEntries)
                        {
                            this.Entries.RemoveAt(0);
                        }

                        entry = entry.TrimEnd(new[] { '\r', '\n' });

                        // Go to great lenths to avoid converting entriesInCategory to a list, as that involves an unnecessary copy...

                        // Logic to display 'last message repeated n times'
                        if (allowRepititionMessage && entriesInCategory.Count() > 1 && entry == entriesInCategory.First().Message)
                        {
                            this.Entries.Add(new LogEntry(1, category));
                        }
                        else if (allowRepititionMessage && entriesInCategory.Count() > 2 && entry == entriesInCategory.Skip(1).Take(1).First().Message && entriesInCategory.First().RepititionCount > 0)
                        {
                            int prevRepitition = entriesInCategory.First().RepititionCount;
                            this.Entries.RemoveAt(this.Entries.Count - 1);
                            this.Entries.Add(new LogEntry(prevRepitition + 1, category));
                        }
                        else if (allowIncompleteMessages && entriesInCategory.Count() > 0 && !entriesInCategory.First().IsComplete)
                        {
                            var oldEntry = entriesInCategory.First();
                            oldEntry.AddMessagePart(entry);
                            oldEntry.IsComplete = isCompleteMessage;
                            this.Entries.Refresh();
                        }
                        else if (entry.Length > 0)
                        {
                            this.Entries.Add(new LogEntry(entry, isCompleteMessage, category));
                        }
                    }
                });
        }
    }
}
