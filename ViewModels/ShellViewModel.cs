using Caliburn.Micro;
using Ec2Manager.Events;
using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Ec2Manager.Classes;

namespace Ec2Manager.ViewModels
{
    [Export]
    class ShellViewModel : Conductor<IScreen>.Collection.OneActive, IHandle<CreateInstanceEvent>
    {
        [ImportingConstructor]
        public ShellViewModel(ConnectViewModel connectModel, IEventAggregator events)
        {
            this.DisplayName = "Ec2Manager";

            events.Subscribe(this);

            this.ActivateItem(connectModel);
        }

        public void Handle(CreateInstanceEvent message)
        {
            var instanceViewModel = IoC.Get<InstanceViewModel>();
            instanceViewModel.Manager = message.Manager;
            instanceViewModel.InstanceAmi = message.InstanceAmi;
            instanceViewModel.InstanceSize = message.InstanceSize;
            instanceViewModel.LoginAs = message.LoginAs;
            instanceViewModel.AvailabilityZone = message.AvailabilityZone;

            this.ActivateItem(instanceViewModel);
        }
    }
}
