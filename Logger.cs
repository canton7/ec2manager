using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Ec2Manager
{
    class Logger
    {
        public delegate void NewLogEntryEventHandler(object sender, NewLogEntryEventArgs e);
        public event NewLogEntryEventHandler NewLogEntry;

        public Logger()
        {
        }

        public void LogFromStream(Stream stream, IAsyncResult asynch)
        {
            using (var sr = new StreamReader(stream))
            {
                while (!asynch.IsCompleted)
                {
                    var result = sr.ReadToEnd();
                    if (string.IsNullOrEmpty(result))
                        continue;
                    this.newLogEntry(result);
                }
            }
        }

        private void newLogEntry(string text)
        {
            var handler = this.NewLogEntry;
            if (handler != null)
                handler(this, new NewLogEntryEventArgs(text));
        }

        public class NewLogEntryEventArgs : EventArgs
        {
            public string Text { get; private set; }
            public NewLogEntryEventArgs(string text) : base()
	        {
                this.Text = text;
	        }
        }
    }
}
