using Ec2Manager.Classes;
using Ec2Manager.Properties;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace Ec2Manager.Utilities
{
    public class VersionManager
    {
        private AsyncLazy<Version> currentVersion;

        /// <summary>
        /// Warning! Will block if the current version is not already cached
        /// </summary>
        public Version CurrentVersion
        {
            get { return this.currentVersion.Value.Result; }
        }

        public Version OurVersion
        {
            get { return Assembly.GetExecutingAssembly().GetName().Version; }
        }

        public VersionManager()
        {
            this.currentVersion = new AsyncLazy<Version>(async () =>
                {
                    Version result = null;

                    try
                    {
                        WebClient client = new WebClient();
                        var parts = (await client.DownloadStringTaskAsync(Settings.Default.CurrentVersionUrl)).Split(new[] { '.' }).Select(x => Int32.Parse(x)).ToArray();
                        result = new Version(parts[0], parts[1], parts[2]);
                    }
                    catch (WebException)
                    {
                    }

                    return result;
                });
        }

        public async Task<bool> IsUpToDateAsync()
        {
            var ourVersion = Assembly.GetExecutingAssembly().GetName().Version;
            var currentVersion = await this.currentVersion.Value;

            // If it's null, then we can't get the latest version. Just say we're up to date
            if (currentVersion == null)
                return true;

            return currentVersion <= ourVersion;
        }

        public Task<Version> GetCurrentVersionAsync()
        {
            return this.currentVersion.Value;
        }
    }
}
