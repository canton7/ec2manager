using Caliburn.Micro;
using Ec2Manager.Configuration;
using Ec2Manager.Properties;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Ec2Manager.ViewModels
{
    public class ReconnectDetailsViewModel : Screen
    {
        private Config config;

        private string loginAs = Settings.Default.DefaultLogonUser;
        public string LoginAs
        {
            get { return this.loginAs; }
            set
            {
                this.loginAs = value;
                this.NotifyOfPropertyChange();
                this.NotifyOfPropertyChange(() => CanContinue);
            }
        }

        private string privateKeyFile;
        public string PrivateKeyFile
        {
            get { return this.privateKeyFile; }
            set
            {
                this.privateKeyFile = value;
                this.NotifyOfPropertyChange();
                this.NotifyOfPropertyChange(() => CanContinue);
            }
        }

        public string PrivateKey
        {
            get { return File.ReadAllText(this.PrivateKeyFile); }
        }

        public ReconnectDetailsViewModel(Config config)
        {
            this.DisplayName = "Browse for key";

            this.config = config;
        }

        public void BrowsePrivateKeyFile()
        {
            var dialog = new OpenFileDialog();
            var result = dialog.ShowDialog();
            if (result.HasValue && result.Value && File.Exists(dialog.FileName))
            {
                this.PrivateKeyFile = dialog.FileName;
                var user = this.config.ParseUserFromKey(this.PrivateKey);
                if (user != null)
                    this.LoginAs = user;
            }
        }

        public bool CanContinue
        {
            get
            {
                return !string.IsNullOrWhiteSpace(this.LoginAs)
                    && !string.IsNullOrWhiteSpace(this.PrivateKeyFile)
                    && File.Exists(this.PrivateKeyFile);
            }
        }
        public void Continue()
        {
            this.TryClose(true);
        }
    }
}
