using Ec2Manager.Classes;
using Ec2Manager.Properties;
using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace Ec2Manager.Configuration
{
    [Export]
    [PartCreationPolicy(CreationPolicy.Shared)]
    public class Config
    {
        private readonly string appDataFolder = "Ec2Manager"; 

        public string ConfigDir
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), appDataFolder); }
        }

        public string SnapshotConfigFile
        {
            get { return Path.Combine(this.ConfigDir, "snapshot-config.xml"); }
        }

        private AsyncLazy<SnapshotConfig> snapshotConfig;
        public Task<SnapshotConfig> GetSnapshotConfigAsync()
        {
            return this.snapshotConfig.Value;
        }


        [ImportingConstructor]
        public Config()
        {
            Directory.CreateDirectory(this.ConfigDir);

            this.snapshotConfig = new AsyncLazy<SnapshotConfig>(async () =>
                {
                    SnapshotConfig webConfig;
                    SnapshotConfig localConfig;
                    try
                    {
                        WebClient client = new WebClient();
                        webConfig = SnapshotConfig.FromString(await client.DownloadStringTaskAsync(Settings.Default.SnapshotConfigUrl));
                    }
                    catch (WebException)
                    {
                        webConfig = new SnapshotConfig();
                    }

                    try
                    {
                        localConfig = SnapshotConfig.FromFile(this.SnapshotConfigFile);
                    }
                    catch (FileNotFoundException)
                    {
                        localConfig = new SnapshotConfig();
                    }

                    return new SnapshotConfig(webConfig.Snapshots.Concat(localConfig.Snapshots));
                });
        }
    }
}
