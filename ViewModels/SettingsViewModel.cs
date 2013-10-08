using Caliburn.Micro;
using Ec2Manager.Configuration;
using Ec2Manager.Properties;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Ec2Manager.ViewModels
{
    public class SettingsViewModel : Screen
    {
        private Config config;
        public MainConfig MainConfig
        {
            get { return this.config.MainConfig; }
        }

        public SettingsViewModel(Config config)
        {
            this.DisplayName = "Settings";
            this.config = config;
        }

        public void Save()
        {
            this.config.SaveMainConfig();
            this.TryClose();
        }

        public void Cancel()
        {
            this.config.DiscardMainConfig();
        }

        public void ShowCredentials()
        {
            System.Diagnostics.Process.Start(Settings.Default.AwsCredentialsUrl);
        }
    }
}
