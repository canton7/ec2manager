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
using Ec2Manager.Properties;
using System.Diagnostics;
using System.Dynamic;

namespace Ec2Manager.ViewModels
{
    [Export]
    public class ShellViewModel
        : Conductor<IScreen>.Collection.OneActive,
        IHandle<CreateInstanceEvent>, IHandle<TerminateInstanceEvent>, IHandle<ReconnectInstanceEvent>
    {
        private IWindowManager windowManager;
        private Config config;

        // Caliburn micro's target implementation has some weird behaviour, in that if the target's binding changes
        // to null, the target isn't updated. This is the case if we bind to ActiveItem.ActiveItem directly.
        // I've raised an issue, but until this is looked at use the following workaround (return a real value
        // instead of null if ActiveItem.ActiveItem doesn't exist).
        public object SubActiveItem
        {
            get
            {
                if (this.ActiveItem is ConductorBaseWithActiveItem<IScreen>)
                    return ((ConductorBaseWithActiveItem<IScreen>)this.ActiveItem).ActiveItem;
                else
                    return new object();
            }
        }

        [ImportingConstructor]
        public ShellViewModel(ConnectViewModel connectModel, IEventAggregator events, IWindowManager windowManager, Config config)
        {
            this.DisplayName = "Ec2Manager";
            this.windowManager = windowManager;
            this.config = config;

            this.Bind(s => s.ActiveItem, (o, e) => this.NotifyOfPropertyChange(() => SubActiveItem));

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
            this.windowManager.ShowDialog(IoC.Get<SettingsViewModel>(), settings: new Dictionary<string, object>()
                {
                    { "ResizeMode", ResizeMode.NoResize },
                });
        }

        public void ShowEc2Console()
        {
            Process.Start(Settings.Default.Ec2ConsoleUrl);
        }

        public void ShowEc2Pricing()
        {
            Process.Start(Settings.Default.Ec2PricingUrl);
        }

        public void ShowAbout()
        {
            this.windowManager.ShowDialog(IoC.Get<AboutViewModel>(), settings: new Dictionary<string, object>()
                {
                    { "WindowStyle", WindowStyle.None },
                    { "ShowInTaskbar", false},
                });
        }

        public async void Handle(CreateInstanceEvent message)
        {
            var instanceViewModel = IoC.Get<InstanceViewModel>();

            instanceViewModel.Bind(s => s.ActiveItem, (o, e) => this.NotifyOfPropertyChange(() => SubActiveItem));
            this.ActivateItem(instanceViewModel);

            await instanceViewModel.SetupAsync(message.Instance, message.LoginAs);
        }

        public async void Handle(TerminateInstanceEvent message)
        {
            var terminateViewModel = IoC.Get<TerminateInstanceViewModel>();
            this.ActivateItem(terminateViewModel);

            await terminateViewModel.SetupAsync(message.Instance);
        }

        public async void Handle(ReconnectInstanceEvent message)
        {
            var instanceViewModel = IoC.Get<InstanceViewModel>();

            instanceViewModel.Bind(s => s.ActiveItem, (o, e) => this.NotifyOfPropertyChange(() => SubActiveItem));
            this.ActivateItem(instanceViewModel);

            await instanceViewModel.ReconnectAsync(message.Instance);
        }
    }
}
