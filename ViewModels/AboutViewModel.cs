﻿using Stylet;
using Ec2Manager.Properties;
using Ec2Manager.Utilities;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace Ec2Manager.ViewModels
{
    public class AboutViewModel : Screen
    {
        public string Version { get; private set; }
        public string HomepageUrl { get; set; }

        public AboutViewModel (VersionManager versionManager)
	    {
            this.DisplayName = "About";

            this.Version = versionManager.OurVersion.ToString(3);
            this.HomepageUrl = Settings.Default.HomePageUrl;
	    }

        public void ShowHomepage()
        {
            Process.Start(Settings.Default.HomePageUrl);
        }

        public void Close()
        {
            this.TryClose();
        }
    }
}
