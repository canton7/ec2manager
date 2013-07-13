﻿using Caliburn.Micro;
using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading;

namespace Ec2Manager.Utilities
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
            this.newLogEntry(message.TrimEnd());
        }

        public void Log(string format, params string[] parameters)
        {
            this.newLogEntry(String.Format(format, parameters).TrimEnd());
        }

        public void LogFromStream(IAsyncResult asynch, Stream stdout, Stream stderr = null, CancellationToken? cancellationToken = null)
        {
            CancellationToken token = cancellationToken.HasValue ? cancellationToken.Value : new CancellationToken();

            using (var outsr = new StreamReader(stdout))
            using (var errsr = new StreamReader(stderr))
            {
                while (true)
                {
                    token.ThrowIfCancellationRequested();

                    var outText = outsr.ReadToEnd();
                    var errText = errsr.ReadToEnd();

                    if (string.IsNullOrEmpty(outText) && string.IsNullOrEmpty(errText))
                    {
                        if (asynch.IsCompleted)
                            break;
                        else
                            Thread.Sleep(100);
                    }
                    else
                    {
                        if (!string.IsNullOrEmpty(outText))
                            this.newLogEntry(outText, true, true);

                        if (!string.IsNullOrEmpty(errText))
                            this.newLogEntry(errText, true, true);
                    }
                }
            }
        }

        private void newLogEntry(string text, bool allowRepititionMessages = false, bool allowIncompleteMessages = false)
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
        }
    }
}