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
    public class InstanceViewModel : Conductor<IScreen>.Collection.OneActive
    {
        public Ec2Manager Manager { get; set; }
        public string InstanceSize { get; set; }
        public string InstanceAmi { get; set; }

        private Logger logger;

        [ImportingConstructor]
        public InstanceViewModel(InstanceDetailsViewModel instanceDetailsModel, Logger logger)
        {
            this.logger = logger;
            instanceDetailsModel.Logger = logger;

            this.ActivateItem(instanceDetailsModel);
        }

        protected override void OnInitialize()
        {
            base.OnInitialize();

            this.DisplayName = this.Manager.Name;
            this.Manager.Logger = this.logger;
            var creationTask = this.Manager.CreateAsync(this.InstanceAmi, this.InstanceSize);
        }
    }
}
