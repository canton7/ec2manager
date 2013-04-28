using Caliburn.Micro;
using Ec2Manager.Events;
using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Ec2Manager.Classes;
using System.Windows;
using Ec2Manager.Configuration;

namespace Ec2Manager.ViewModels
{
    [Export]
    public class ShellViewModel : Conductor<IScreen>.Collection.OneActive, IHandle<CreateInstanceEvent>, IHandle<TerminateInstanceEvent>
    {
        private IWindowManager windowManager;
        private Config config;

        [ImportingConstructor]
        public ShellViewModel(ConnectViewModel connectModel, IEventAggregator events, IWindowManager windowManager, Config config)
        {
            this.DisplayName = "Ec2Manager";
            this.windowManager = windowManager;
            this.config = config;

            events.Subscribe(this);

            this.ActivateItem(connectModel);
        }

        protected override void OnViewLoaded(object view)
        {
            if (this.config.NeedToUpdateMainConfig())
            {
                // This horrible hack gives the UI thread enough time to render the page before displaying the dialog
                // TODO: Find a better way of doing this
                Task.Run(() =>
                    {
                        this.Invoke(() =>
                            {
                                var result = MessageBox.Show(Application.Current.MainWindow, "Do you want to set your AWS credentials? You will need to do this before you'll be able to do anything else.", "Set AWS credentials?", MessageBoxButton.YesNo, MessageBoxImage.Question);
                                if (result == MessageBoxResult.Yes)
                                    this.ShowSettings();
                            });
                    });
            }
        }

        public override void CanClose(Action<bool> callback)
        {
            if (this.Items.Count > 1)
            {
                var result = MessageBox.Show(Application.Current.MainWindow, "Are you sure you want to close Ec2Manager? If you have running instances you will not be able to reconnect to them, and they will cost you money!", "Are you sure?", MessageBoxButton.YesNo, MessageBoxImage.Question);
                callback(result == MessageBoxResult.Yes);
            }
            else
            {
                callback(true);
            }
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
