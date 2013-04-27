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
            get { return Path.Combine(this.ConfigDir, "snapshot-config.txt"); }
        }

        private AsyncLazy<IEnumerable<VolumeType>> snapshotConfig;
        public Task<IEnumerable<VolumeType>> GetSnapshotConfigAsync()
        {
            return this.snapshotConfig.Value;
        }


        [ImportingConstructor]
        public Config()
        {
            Directory.CreateDirectory(this.ConfigDir);

            this.snapshotConfig = new AsyncLazy<IEnumerable<VolumeType>>(async () =>
                {
                    var config = new List<VolumeType>();
                    if (File.Exists(this.SnapshotConfigFile))
                    {
                        config.AddRange(File.ReadAllLines(this.SnapshotConfigFile).Where(x => !x.StartsWith(";") && !string.IsNullOrWhiteSpace(x)).Select(x =>
                        {
                            var parts = x.Split(new[] { ' ' }, 2);
                            return new VolumeType(parts[0].Trim(), parts[1].Trim());
                        }));
                    }

                    try
                    {
                        WebClient client = new WebClient();
                        config.AddRange((await client.DownloadStringTaskAsync(Settings.Default.SnapshotConfigUrl)).Split(new[] { "\n", "\r\n" }, StringSplitOptions.RemoveEmptyEntries).Where(x => !x.StartsWith(";")).Select(x =>
                        {
                            var parts = x.Split(new[] { ' ' }, 2);
                            return new VolumeType(parts[0].Trim(), parts[1].Trim());
                        }));
                    }
                    catch (WebException)
                    {
                    }

                    return config;
                });
        }
    }
}
