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
    public class TerminateInstanceViewModel : Screen
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

        private Ec2Manager manager;
        public Ec2Manager Manager
        {
            get { return this.manager; }
            private set
            {
                this.manager = value;
                this.NotifyOfPropertyChange();
            }
        }

        [ImportingConstructor]
        public TerminateInstanceViewModel(Logger logger)
        {
            this.Logger = logger;

            this.DisplayName = "Terminating Instance";
        }

        public async Task SetupAsync(Ec2Manager manager)
        {
            this.Manager = manager;
            await this.Manager.DestroyAsync(this.Logger);
        }
    }
}
