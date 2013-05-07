using Caliburn.Micro;
using Ec2Manager.Classes;
using Ec2Manager.Configuration;
using Ec2Manager.Events;
using Ec2Manager.Properties;
using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Ec2Manager.ViewModels
{
    [Export]
    public class ConnectViewModel : Screen
    {
        private static readonly LabelledValue[] instanceTypes = new LabelledValue[]
        {
            new LabelledValue("M1 Small", "m1.small"),
            new LabelledValue("M1 Medium", "m1.medium"),
            new LabelledValue("M1 Large", "m1.large"),
            new LabelledValue("M3 Extra Large", "m3.xlarge"),
            new LabelledValue("M3 2x Extra Large", "m3.2xlarge"),
            new LabelledValue("Micro", "t1.micro"),
            new LabelledValue("High-Memory Extra Large", "m2.xlarge"),
            new LabelledValue("High-Memory 2x Extra Large", "m2.2xlarge"),
            new LabelledValue("High-Memory 4x Extra Large", "m2.4xlarge"),
            new LabelledValue("High-CPU Medium", "c1.medium"),
            new LabelledValue("High-CPU Extra Large", "c1.xlarge"),
        };
        private static readonly LabelledValue[] availabilityZones = new LabelledValue[]
        {
            new LabelledValue("Any", null),
            new LabelledValue("eu-west-1a", "eu-west-1a"),
            new LabelledValue("eu-west-1b", "eu-west-1b"),
            new LabelledValue("eu-west-1c", "eu-west-1c"),
        };

        private Config config;

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

        private string loginAs;
        public string LoginAs
        {
            get { return this.loginAs; }
            set
            {
                this.loginAs = value;
                this.NotifyOfPropertyChange();
                this.NotifyOfPropertyChange(() => CanCreate);
            }
        }

        public LabelledValue[] InstanceTypes
        {
            get { return instanceTypes; }
        }
        public LabelledValue ActiveInstanceType { get; set; }

        private string ami;
        public string AMI
        {
            get { return this.ami; }
            set
            {
                this.ami = value;
                this.NotifyOfPropertyChange();
                this.NotifyOfPropertyChange(() => CanCreate);
            }
        }

        public LabelledValue[] AvailabilityZones
        {
            get { return availabilityZones; }
        }

        private LabelledValue selectedAvailabilityZone = availabilityZones[0];
        public LabelledValue SelectedAvailabilityZone
        {
            get { return this.selectedAvailabilityZone; }
            set
            {
                this.selectedAvailabilityZone = value;
                this.NotifyOfPropertyChange();
            }
        }

        private LabelledValue[] runningInstances = new[] { new LabelledValue("Loading...", null) };
        public LabelledValue[] RunningInstances
        {
            get { return this.runningInstances; }
            set
            {
                this.runningInstances = value;
                this.NotifyOfPropertyChange();
            }
        }

        private LabelledValue activeRunningInstance;
        public LabelledValue ActiveRunningInstance
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

        private IEventAggregator events;

        [ImportingConstructor]
        public ConnectViewModel(Config config, IEventAggregator events)
        {
            this.config = config;
            this.events = events;

            this.config.Bind(s => s.MainConfig, (o, e) => 
                {
                    this.LoadFromConfig();
                    this.NotifyOfPropertyChange(() => CanCreate);
                    this.NotifyOfPropertyChange(() => CanRefreshRunningInstances);
                    this.NotifyOfPropertyChange(() => CanTerminateInstance);
                    Task.Run(() => this.RefreshRunningInstances());
                });

            this.DisplayName = "Create New Instance";
            this.LoadFromConfig();

            this.ActiveInstanceType = this.InstanceTypes.FirstOrDefault(x => x.Value == "t1.micro");
            this.ActiveRunningInstance = this.RunningInstances[0];

            Task.Run(() => this.RefreshRunningInstances());
        }

        private void LoadFromConfig()
        {
            this.AMI = this.config.MainConfig.DefaultAmi;
            this.LoginAs = this.config.MainConfig.DefaultLogonUser;
        }

        public bool CanCreate
        {
            get
            {
                return !string.IsNullOrWhiteSpace(this.config.MainConfig.AwsAccessKey) &&
                    !string.IsNullOrWhiteSpace(this.config.MainConfig.AwsSecretKey) &&
                    !string.IsNullOrWhiteSpace(this.InstanceName) &&
                    !string.IsNullOrWhiteSpace(this.LoginAs) &&
                    !string.IsNullOrWhiteSpace(this.AMI);
            }
        }
        public void Create()
        {
            var manager = new Ec2Manager(this.config.MainConfig.AwsAccessKey, this.config.MainConfig.AwsSecretKey);
            manager.Name = this.InstanceName;
            this.events.Publish(new CreateInstanceEvent()
            {
                InstanceAmi = this.AMI,
                InstanceSize = this.ActiveInstanceType.Value,
                Manager = manager,
                LoginAs = this.LoginAs,
                SpotBidAmount = this.UseSpotMarket ? (double?)this.SpotBidAmount : null,
                AvailabilityZone = this.SelectedAvailabilityZone.Value,
            });
        }

        public bool CanRefreshRunningInstances
        {
            get
            {
                return !string.IsNullOrWhiteSpace(this.config.MainConfig.AwsAccessKey) &&
                    !string.IsNullOrWhiteSpace(this.config.MainConfig.AwsSecretKey);
            }
        }
        public void RefreshRunningInstances()
        {
            if (!this.CanRefreshRunningInstances)
            {
                this.RunningInstances = new[] { new LabelledValue("Can't load. Try refreshing", null) };
                this.ActiveRunningInstance = this.RunningInstances[0];
                return;
            }

            try
            {
                var manager = new Ec2Manager(this.config.MainConfig.AwsAccessKey, this.config.MainConfig.AwsSecretKey);
                this.RunningInstances = manager.ListInstances().Select(x => new LabelledValue(x.Item2, x.Item1)).ToArray();
                if (this.RunningInstances.Length == 0)
                {
                    this.RunningInstances = new[] { new LabelledValue("No Running Instances", null) };
                }
                this.ActiveRunningInstance = this.RunningInstances[0];
            }
            catch (Exception)
            {
                this.RunningInstances = new[] { new LabelledValue("Error loading. Bad credentials?", null) };
                this.ActiveRunningInstance = this.RunningInstances[0];
            }
        }

        public bool CanReconnectInstance
        {
            get
            {
                return !string.IsNullOrWhiteSpace(this.config.MainConfig.AwsAccessKey) &&
                    !string.IsNullOrWhiteSpace(this.config.MainConfig.AwsSecretKey) &&
                    this.ActiveRunningInstance != null && !string.IsNullOrEmpty(this.ActiveRunningInstance.Value);
            }
        }
        public void ReconnectInstance()
        {
            var manager = new Ec2Manager(this.config.MainConfig.AwsAccessKey, this.config.MainConfig.AwsSecretKey, this.ActiveRunningInstance.Value);
            manager.Name = this.ActiveRunningInstance.Label;
            events.Publish(new ReconnectInstanceEvent()
            {
                Manager = manager,
            });
        }

        public bool CanTerminateInstance
        {
            get
            {
                return !string.IsNullOrWhiteSpace(this.config.MainConfig.AwsAccessKey) &&
                    !string.IsNullOrWhiteSpace(this.config.MainConfig.AwsSecretKey) &&
                    this.ActiveRunningInstance != null && !string.IsNullOrEmpty(this.ActiveRunningInstance.Value);
            }
        }
        public void TerminateInstance()
        {
            var manager = new Ec2Manager(this.config.MainConfig.AwsAccessKey, this.config.MainConfig.AwsSecretKey, this.ActiveRunningInstance.Value);
            manager.Name = this.ActiveRunningInstance.Label;
            events.Publish(new TerminateInstanceEvent()
            {
                Manager = manager,
            });
        }
    }
}
