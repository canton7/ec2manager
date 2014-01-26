using Caliburn.Micro;
using Ec2Manager.Configuration;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Ec2Manager.ViewModels
{
    public class ManageFriendsViewModel
    {
        private Config config;

        public BindableCollection<Friend> Friends { get; private set; }

        public ManageFriendsViewModel(Config config)
        {
            this.config = config;
            this.Friends = new BindableCollection<Friend>(config.FriendsWithoutDefaults);
        }
    }
}
