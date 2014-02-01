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

        public string KeyPath
        {
            get { return Path.Combine(this.ConfigDir, "ec2key", "eu-west-1"); }
        }


        public string MainConfigFile
        {
            get { return Path.Combine(this.ConfigDir, "config.xml"); }
        }

        public MainConfig MainConfig { get; private set; }

        public Config()
        {
            Directory.CreateDirectory(this.ConfigDir);

            this.LoadMainConfig();
        }

        public IEnumerable<Friend> DefaultFriends
        {
            get
            {
                var defaults = new List<Friend>() { new Friend("self", "Your Images") };
                if (this.MainConfig.ShowOfficialImages)
                    defaults.Add(new Friend(Settings.Default.DefaultImagesUserId, "Official Images"));
                return defaults;
            }
        }

        public IEnumerable<Friend> FriendsWithoutDefaults
        {
            get { return this.MainConfig.Friends; }
            set { this.MainConfig.Friends = value.ToList(); }
        }

        public IEnumerable<Friend> Friends
        {
            get { return this.DefaultFriends.Concat(this.FriendsWithoutDefaults); }
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

        public void SaveKey(KeyDescription key)
        {
            var keyPath = this.KeyPath;

            var commentedKey = Regex.Replace(key.Key, @"(?=-----END)", String.Format("Ec2ManagerFingerprint:{0}\n", key.Fingerprint));

            Directory.CreateDirectory(Path.GetDirectoryName(keyPath));
            File.WriteAllText(keyPath, commentedKey);
        }

        public KeyDescription? LoadKey()
        {
            var keyPath = this.KeyPath;

            if (!File.Exists(keyPath))
                return null;
            
            return this.ParseKeyAtPath(keyPath);
        }

        public KeyDescription ParseKeyAtPath(string keyPath)
        {
            var keyMaterial = File.ReadAllText(keyPath);
            var fingerprint = Regex.Match(keyMaterial, @"Ec2ManagerFingerprint:(\S*)").Groups[1].Value;
            // Strip comment from key, as SshNet doesn't like them
            keyMaterial = Regex.Replace(keyMaterial, @"^Ec2Manager.*\r?\n", "", RegexOptions.Multiline);

            return new KeyDescription(keyMaterial, fingerprint);
        }
    }
}
