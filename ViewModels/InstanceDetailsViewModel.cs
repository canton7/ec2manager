using Caliburn.Micro;
using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Ec2Manager.ViewModels
{
    [Export]
    [PartCreationPolicy(CreationPolicy.NonShared)]
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

        [ImportingConstructor]
        public InstanceDetailsViewModel()
        {
            this.DisplayName = "Instance";
        }
    }
}
