using Caliburn.Micro;
using Ec2Manager.Classes;
using Ec2Manager.Properties;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Ec2Manager.Configuration
{
    public class Config : PropertyChangedBase 
    {
        private readonly string appDataFolder = "Ec2Manager"; 

        public string ConfigDir
        {
            get
            {
                var executableDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                if (executableDirectory.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles)))
                    return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), appDataFolder);
                else
                    return Path.Combine(executableDirectory, "config");
            }
        }

        public string MainConfigFile
        {
            get { return Path.Combine(this.ConfigDir, "config.xml"); }
        }

        public MainConfig MainConfig { get; private set; }

        public string SavedKeysDir
        {
            get { return Path.Combine(this.ConfigDir, "keys"); }
        }

        public Config()
        {
            Directory.CreateDirectory(this.ConfigDir);
            Directory.CreateDirectory(this.SavedKeysDir);

            this.LoadMainConfig();
        }

        public IList<Friend> Friends
        {
            get
            {
                var friends = new List<Friend>() { new Friend(Settings.Default.DefaultImagesUserId, "Official Images") };
                return friends;
            }
        }

        private void LoadMainConfig()
        {
            if (File.Exists(this.MainConfigFile))
            {
                this.MainConfig = MainConfig.FromFile(this.MainConfigFile);
            }
            else
            {
                this.MainConfig = new MainConfig();
            }
        }

        public bool NeedToUpdateMainConfig()
        {
            return string.IsNullOrWhiteSpace(this.MainConfig.AwsAccessKey) || string.IsNullOrWhiteSpace(this.MainConfig.AwsSecretKey);
        }

        public void SaveMainConfig()
        {
            this.MainConfig.SaveToFile(this.MainConfigFile);
            this.NotifyOfPropertyChange(() => MainConfig);
        }

        public void DiscardMainConfig()
        {
            this.LoadMainConfig();
        }

        public void SaveKeyAndUser(string name, string user, string privateKey)
        {
            var commentedKey = Regex.Replace(privateKey, @"(?=-----END)", "Ec2ManagerUser:" + user + "\n");
            File.WriteAllText(Path.Combine(this.SavedKeysDir, name), commentedKey);
        }

        public Tuple<string, string> RetrieveKeyAndUser(string name)
        {
            var path = Path.Combine(this.SavedKeysDir, name);
            var key = File.ReadAllText(path);
            var user = this.ParseUserFromKey(key);
            // Strip comment from key, as SshNet doesn't like them
            key = Regex.Replace(key, @"^Ec2Manager.*\r?\n", "", RegexOptions.Multiline);
            return new Tuple<string, string>(key, user);
        }

        public string ParseUserFromKey(string key)
        {
            var user = Regex.Match(key, @"Ec2ManagerUser:(\w*)").Groups[1].Value;
            return string.IsNullOrWhiteSpace(user) ? null : user;
        }
    }
}
