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
    public class ShellViewModel : Conductor<IScreen>.Collection.OneActive, IHandle<CreateInstanceEvent>, IHandle<TerminateInstanceEvent>
    {
        private IWindowManager windowManager;

        [ImportingConstructor]
        public ShellViewModel(ConnectViewModel connectModel, IEventAggregator events, IWindowManager windowManager)
        {
            this.DisplayName = "Ec2Manager";
            this.windowManager = windowManager;

            events.Subscribe(this);

            this.ActivateItem(connectModel);
        }

        public void ShowSettings()
        {
            this.windowManager.ShowDialog(IoC.Get<SettingsViewModel>());
        }

        public async void Handle(CreateInstanceEvent message)
        {
            var instanceViewModel = IoC.Get<InstanceViewModel>();
            this.ActivateItem(instanceViewModel);

            await instanceViewModel.SetupAsync(message.Manager, message.InstanceAmi, message.InstanceSize, message.LoginAs, message.AvailabilityZone);
        }

        public async void Handle(TerminateInstanceEvent message)
        {
            var terminateViewModel = IoC.Get<TerminateInstanceViewModel>();
            this.ActivateItem(terminateViewModel);

            await terminateViewModel.SetupAsync(message.Manager);
        }
    }
}
