﻿using Stylet;
using Ec2Manager.Events;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Ec2Manager.Classes;
using System.Windows;
using Ec2Manager.Configuration;
using Ec2Manager.Properties;
using System.Diagnostics;
using System.Dynamic;
using Ec2Manager.Utilities;
using Ec2Manager.Ec2Manager;

namespace Ec2Manager.ViewModels
{
    public class ShellViewModel
        : Conductor<IScreen>.Collections.OneActive,
        IHandle<CreateInstanceEvent>, IHandle<TerminateInstanceEvent>, IHandle<ReconnectInstanceEvent>
    {
        private IWindowManager windowManager;
        private Config config;
        private Ec2Connection connection;
        private VersionManager versionManager;
        private IInstanceViewModelFactory instanceViewModelFactory;
        private ITerminateInstanceViewModelFactory terminateInstanceViewModelFactory;

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

        public ShellViewModel(ConnectViewModel connectModel,
            IEventAggregator events,
            IWindowManager windowManager,
            Config config,
            Ec2Connection connection,
            VersionManager versionManager,
            IInstanceViewModelFactory instanceViewModelFactory,
            ITerminateInstanceViewModelFactory terminateInstanceViewModelFactory)
        {
            this.DisplayName = "Ec2Manager";
            this.windowManager = windowManager;
            this.config = config;
            this.connection = connection;
            this.versionManager = versionManager;
            this.instanceViewModelFactory = instanceViewModelFactory;
            this.terminateInstanceViewModelFactory = terminateInstanceViewModelFactory;

            this.Bind(s => s.ActiveItem, (o, e) => this.NotifyOfPropertyChange(() => SubActiveItem));
            this.connection.Bind(s => s.IsConnected, (o, e) => this.NotifyOfPropertyChange(() => CanManageFriends));

            events.Subscribe(this);

            this.ActivateItem(connectModel);
        }

        protected override void OnViewLoaded()
        {
            this.CheckForUpdate();

            if (this.config.NeedToUpdateMainConfig())
            {
                // This horrible hack gives the UI thread enough time to render the page before displaying the dialog
                // TODO: Find a better way of doing this
                Task.Run(() =>
                    {
                        Execute.OnUIThread(() =>
                            {
                                var result = MessageBox.Show(Application.Current.MainWindow, "Do you want to set your AWS credentials? You will need to do this before you'll be able to do anything else.", "Set AWS credentials?", MessageBoxButton.YesNo, MessageBoxImage.Question);
                                if (result == MessageBoxResult.Yes)
                                    this.ShowSettings();
                            });
                    });
            }
        }

        public async void CheckForUpdate(bool dontAltertIfNoVersionAvailable = true)
        {
            if (!await this.versionManager.IsUpToDateAsync())
            {
                Execute.OnUIThread(() =>
                {
                    var result = MessageBox.Show(Application.Current.MainWindow, "A new version is available!\nWould you like to download version " + this.versionManager.CurrentVersion.ToString(3) + "?", "New version!", MessageBoxButton.YesNo, MessageBoxImage.Question);
                    if (result == MessageBoxResult.Yes)
                    {
                        Process.Start(Settings.Default.InstallerDownloadUrl);
                    }
                });
            }
            else if (!dontAltertIfNoVersionAvailable)
            {
                MessageBox.Show(Application.Current.MainWindow, "The current version " + this.versionManager.OurVersion.ToString(3) + " is up to date.", "Up to date", MessageBoxButton.OK);
            }
        }

        public override Task<bool> CanCloseAsync()
        {
            if (this.Items.Count > 1)
            {
                var result = MessageBox.Show(Application.Current.MainWindow, "Are you sure you want to close Ec2Manager? If you have running instances you will not be able to reconnect to them, and they will cost you money!", "Are you sure?", MessageBoxButton.YesNo, MessageBoxImage.Question);
                return Task.FromResult(result == MessageBoxResult.Yes);
            }
            else
            {
                return Task.FromResult(true);
            }
        }

        public bool CanManageFriends
        {
            get { return this.connection.IsConnected; }
        }

        public void ManageFriends()
        {
            this.windowManager.ShowDialog<ManageFriendsViewModel>();
        }

        public void ShowSettings()
        {
            this.windowManager.ShowDialog<SettingsViewModel>();
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
            this.windowManager.ShowDialog<AboutViewModel>();
        }

        public async void Handle(CreateInstanceEvent message)
        {
            var instanceViewModel = this.instanceViewModelFactory.CreateInstanceViewModel();

            instanceViewModel.Bind(s => s.ActiveItem, (o, e) => this.NotifyOfPropertyChange(() => SubActiveItem));
            this.ActivateItem(instanceViewModel);

            await instanceViewModel.SetupAsync(message.Instance, message.LoginAs);
        }

        public async void Handle(TerminateInstanceEvent message)
        {
            var terminateViewModel = this.terminateInstanceViewModelFactory.CreateTerminateInstanceViewModel();
            this.ActivateItem(terminateViewModel);

            await terminateViewModel.SetupAsync(message.Instance);
        }

        public async void Handle(ReconnectInstanceEvent message)
        {
            var instanceViewModel = this.instanceViewModelFactory.CreateInstanceViewModel();

            instanceViewModel.Bind(s => s.ActiveItem, (o, e) => this.NotifyOfPropertyChange(() => SubActiveItem));
            this.ActivateItem(instanceViewModel);

            await instanceViewModel.ReconnectAsync(message.Instance);
        }
    }

    public interface IInstanceViewModelFactory
    {
        InstanceViewModel CreateInstanceViewModel();
    }

    public interface ITerminateInstanceViewModelFactory
    {
        TerminateInstanceViewModel CreateTerminateInstanceViewModel();
    }
}
