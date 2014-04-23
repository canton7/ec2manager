using Stylet;
using Ec2Manager.Utilities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Ec2Manager.ViewModels
{
    public class InstanceDetailsViewModel : Screen
    {
        private Logger logger;
        public Logger Logger
        {
            get { return this.logger; }
            set
            {
                this.logger = value;
                this.NotifyOfPropertyChange();
            }
        }

        public InstanceDetailsViewModel()
        {
            this.DisplayName = "Instance";
        }
    }
}
