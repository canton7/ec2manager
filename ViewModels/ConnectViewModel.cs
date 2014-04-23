using Stylet;
using Ec2Manager.Classes;
using Ec2Manager.Configuration;
using Ec2Manager.Events;
using Ec2Manager.Properties;
using Ec2Manager.Ec2Manager;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using Ec2Manager.Model;

namespace Ec2Manager.ViewModels
{
    public class ConnectViewModel : Screen
    {
        private static readonly LabelledValue<string>[] availabilityZones = new LabelledValue<string>[]
        {
            new LabelledValue<string>("Any", null),
            new LabelledValue<string>("eu-west-1a", "eu-west-1a"),
            new LabelledValue<string>("eu-west-1b", "eu-west-1b"),
            new LabelledValue<string>("eu-west-1c", "eu-west-1c"),
        };

        private Config config;
        private Ec2Connection connection;

        private string instanceName = "My Server";
        public string InstanceName
        {
            get { return this.instanceName; }
            set
            {
                this.instanceName = value;
                this.NotifyOfPropertyChange();
                this.NotifyOfPropertyChange(() => CanCreate);
            }
        }

        public InstanceSize[] InstanceTypes
        {
            get { return Ec2Connection.InstanceSizes; }
        }

        private InstanceSize activeInstanceType;
        public InstanceSize ActiveInstanceType
        {
            get { return this.activeInstanceType; }
            set
            {
                this.activeInstanceType = value;
                this.NotifyOfPropertyChange();
            }
        }

        public LabelledValue<string>[] AvailabilityZones
        {
            get { return availabilityZones; }
        }

        private LabelledValue<string> selectedAvailabilityZone = availabilityZones[0];
        public LabelledValue<string> SelectedAvailabilityZone
        {
            get { return this.selectedAvailabilityZone; }
            set
            {
                this.selectedAvailabilityZone = value;
                this.NotifyOfPropertyChange();
            }
        }

        private LabelledValue<Ec2Instance>[] runningInstances = new[] { new LabelledValue<Ec2Instance>("Loading...", null) };
        public LabelledValue<Ec2Instance>[] RunningInstances
        {
            get { return this.runningInstances; }
            set
            {
                this.runningInstances = value;
                this.NotifyOfPropertyChange();
            }
        }

        private LabelledValue<Ec2Instance> activeRunningInstance;
        public LabelledValue<Ec2Instance> ActiveRunningInstance
        {
            get { return this.activeRunningInstance; }
            set
            {
                this.activeRunningInstance = value;
                this.NotifyOfPropertyChange();
                this.NotifyOfPropertyChange(() => CanReconnectInstance);
                this.NotifyOfPropertyChange(() => CanTerminateInstance);
            }
        }

        private bool useSpotMarket = false;
        public bool UseSpotMarket
        {
            get { return this.useSpotMarket; }
            set
            {
                this.useSpotMarket = value;
                this.NotifyOfPropertyChange();
            }
        }

        private double spotBidAmount;
        public double SpotBidAmount
        {
            get { return this.spotBidAmount; }
            set
            {
                this.spotBidAmount = value;
                this.NotifyOfPropertyChange();
            }
        }

        private double currentSpotPrice;
        public double CurrentSpotPrice
        {
            get { return this.currentSpotPrice; }
            set
            {
                this.currentSpotPrice = value;
                this.NotifyOfPropertyChange();
                this.CurrentSpotPriceLabel = this.currentSpotPrice.ToString("$0.000");
            }
        }

        private string currentSpotPriceLabel = "Loading...";
        public string CurrentSpotPriceLabel
        {
            get { return this.currentSpotPriceLabel; }
            set
            {
                this.currentSpotPriceLabel = value;
                this.NotifyOfPropertyChange();
            }
        }

        private IEventAggregator events;

        public ConnectViewModel(MainModel model, IEventAggregator events)
        {
            this.config = model.Config;
            this.connection = model.Connection;
            this.events = events;

            this.config.Bind(s => s.MainConfig, (o, e) => 
                {
                    this.LoadFromConfig();
                });

            this.connection.Bind(s => s.IsConnected, (o, e) =>
                {
                    this.RefreshRunningInstances();
                    this.NotifyOfPropertyChange(() => CanRefreshRunningInstances);
                    this.NotifyOfPropertyChange(() => CanCreate);
                    this.NotifyOfPropertyChange(() => CanReconnectInstance);
                    this.NotifyOfPropertyChange(() => CanTerminateInstance);
                    var spotPriceTask = this.RefreshCurrentSpotPriceAsync();
                });

            this.Bind(s => s.ActiveInstanceType, (o, e) => Task.Run(() => this.RefreshCurrentSpotPriceAsync()));

            this.DisplayName = "Create New Instance";
            this.LoadFromConfig();

            this.ActiveInstanceType = this.InstanceTypes.FirstOrDefault(x => x.Key == "t1.micro");
            this.ActiveRunningInstance = this.RunningInstances[0];

            this.RefreshRunningInstances();
            var spotPriceTaskMain = this.RefreshCurrentSpotPriceAsync();
        }

        protected override void OnActivate()
        {
            // Refresh when we get switched to - means that it auto-updates after terminating
            // an instance for example
            this.RefreshRunningInstances();
        }

        private void LoadFromConfig()
        {
        }

        private async Task RefreshCurrentSpotPriceAsync()
        {
            if (string.IsNullOrWhiteSpace(this.config.MainConfig.AwsAccessKey) || string.IsNullOrWhiteSpace(this.config.MainConfig.AwsSecretKey))
            {
                this.CurrentSpotPriceLabel = "Unavailable";
                return;
            }

            try
            {
                this.CurrentSpotPrice = await this.connection.GetCurrentSpotPriceAsync(this.ActiveInstanceType);
            }
            catch (Exception)
            {
                this.CurrentSpotPriceLabel = "Unavailable";
            }
        }

        public bool CanCreate
        {
            get
            {
                return this.connection.IsConnected &&
                    !string.IsNullOrWhiteSpace(this.InstanceName);
            }
        }
        public void Create()
        {
            if (this.UseSpotMarket && this.SpotBidAmount <= this.CurrentSpotPrice)
            {
                var result = MessageBox.Show(Application.Current.MainWindow, "Are you sure about that spot bid amount?\nIt's lower than the current price, so you'll be waiting a long time for it to be fulfilled (like, days).", "Are you sure?", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
                if (result == MessageBoxResult.No)
                    return;
            }
            else if (this.UseSpotMarket && this.SpotBidAmount < this.CurrentSpotPrice * 1.2)
            {
                var result = MessageBox.Show(Application.Current.MainWindow, "Are you sure about that spot bid amount?\nIt's quite similar to the current price, so there's a high(ish) chance your instance will be terminated.", "Are you sure?", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
                if (result == MessageBoxResult.No)
                    return;
            }

            this.events.Publish(new CreateInstanceEvent()
            {
                Instance = this.connection.CreateInstance(this.InstanceName, Settings.Default.AMI, this.ActiveInstanceType, this.SelectedAvailabilityZone.Value, this.UseSpotMarket ? (double?)this.SpotBidAmount : null),
                LoginAs = Settings.Default.LogonUser,
            });
        }

        public bool CanRefreshRunningInstances
        {
            get { return this.connection.IsConnected; }
        }
        public async void RefreshRunningInstances()
        {
            if (!this.CanRefreshRunningInstances)
            {
                this.RunningInstances = new[] { new LabelledValue<Ec2Instance>("Can't load. Try refreshing", null) };
                this.ActiveRunningInstance = this.RunningInstances[0];
                return;
            }

            try
            {
                this.RunningInstances = (await this.connection.ListInstancesAsync()).Select(x => new LabelledValue<Ec2Instance>(x.Name, x)).ToArray();
                if (this.RunningInstances.Length == 0)
                {
                    this.RunningInstances = new[] { new LabelledValue<Ec2Instance>("No Running Instances", null) };
                }
                this.ActiveRunningInstance = this.RunningInstances[0];
            }
            catch (Exception)
            {
                this.RunningInstances = new[] { new LabelledValue<Ec2Instance>("Error loading. Bad credentials?", null) };
                this.ActiveRunningInstance = this.RunningInstances[0];
            }
        }

        public bool CanReconnectInstance
        {
            get
            {
                return this.connection.IsConnected &&
                    this.ActiveRunningInstance != null && this.ActiveRunningInstance.Value != null;
            }
        }
        public void ReconnectInstance()
        {
            events.Publish(new ReconnectInstanceEvent()
            {
                Instance = this.ActiveRunningInstance.Value,
            });
        }

        public bool CanTerminateInstance
        {
            get
            {
                return this.connection.IsConnected &&
                    this.ActiveRunningInstance != null && this.ActiveRunningInstance.Value != null;
            }
        }
        public void TerminateInstance()
        {
            events.Publish(new TerminateInstanceEvent()
            {
                Instance = this.ActiveRunningInstance.Value,
            });
        }
    }
}
