using Caliburn.Micro;
using Ec2Manager.Properties;
using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace Ec2Manager.ViewModels
{
    [Export]
    public class AboutViewModel : Screen
    {
        public string Version { get; private set; }
        public string HomepageUrl { get; set; }

        public AboutViewModel ()
	    {
            this.DisplayName = "About";

            this.Version = Assembly.GetExecutingAssembly().GetName().Version.ToString(3);
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
