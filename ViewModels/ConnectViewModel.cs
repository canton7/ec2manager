using Caliburn.Micro;
using Ec2Manager.Classes;
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

        private string awsAccessKey = Settings.Default.DefaultAwsAccessKey;
        public string AwsAccessKey
        {
            get { return this.awsAccessKey; }
            set
            {
                this.awsAccessKey = value;
                this.NotifyOfPropertyChange();
                this.NotifyOfPropertyChange(() => CanCreate);
                this.NotifyOfPropertyChange(() => CanRefreshTerminatableInstances);
                this.NotifyOfPropertyChange(() => CanTerminateInstance);
            }
        }

        private string awsSecretKey = Settings.Default.DefaultAwsSecretKey;
        public string AwsSecretKey
        {
            get { return this.awsSecretKey; }
            set
            {
                this.awsSecretKey = value;
                this.NotifyOfPropertyChange();
                this.NotifyOfPropertyChange(() => CanCreate);
                this.NotifyOfPropertyChange(() => CanRefreshTerminatableInstances);
                this.NotifyOfPropertyChange(() => CanTerminateInstance);
            }
        }

        private string instanceName;
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

        private string loginAs = Settings.Default.DefaultLoginAs;
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

        private string ami = Settings.Default.DefaultAMI;
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

        private string availabilityZone;
        public string AvailabilityZone
        {
            get { return this.availabilityZone; }
            set
            {
                this.availabilityZone = value;
                this.NotifyOfPropertyChange();
            }
        }

        private LabelledValue[] terminatableInstances = new[] { new LabelledValue("Press Refresh", null) };
        public LabelledValue[] TerminatableInstances
        {
            get { return this.terminatableInstances; }
            set
            {
                this.terminatableInstances = value;
                this.NotifyOfPropertyChange();
            }
        }

        private LabelledValue activeTerminatableInstance;
        public LabelledValue ActiveTerminatableInstance
        {
            get { return this.activeTerminatableInstance; }
            set
            {
                this.activeTerminatableInstance = value;
                this.NotifyOfPropertyChange();
                this.NotifyOfPropertyChange(() => CanTerminateInstance);
            }
        }


        private IEventAggregator events;

        [ImportingConstructor]
        public ConnectViewModel(IEventAggregator events)
        {
            this.events = events;

            this.DisplayName = "Create New Instance";
            this.ActiveInstanceType = this.InstanceTypes[0];
            this.ActiveTerminatableInstance = this.TerminatableInstances[0];
        }


        public bool CanCreate
        {
            get
            {
                return !string.IsNullOrWhiteSpace(this.AwsAccessKey) &&
                    !string.IsNullOrWhiteSpace(this.AwsSecretKey) &&
                    !string.IsNullOrWhiteSpace(this.InstanceName) &&
                    !string.IsNullOrWhiteSpace(this.LoginAs) &&
                    !string.IsNullOrWhiteSpace(this.AMI);
            }
        }
        public void Create()
        {
            var manager = new Ec2Manager(this.AwsAccessKey, this.AwsSecretKey);
            manager.Name = this.InstanceName;
            events.Publish(new CreateInstanceEvent()
            {
                InstanceAmi = this.AMI,
                InstanceSize = this.ActiveInstanceType.Value,
                Manager = manager,
                LoginAs = this.LoginAs,
                AvailabilityZone = this.AvailabilityZone,
            });
        }

        public bool CanRefreshTerminatableInstances
        {
            get
            {
                return !string.IsNullOrWhiteSpace(this.awsAccessKey) &&
                    !string.IsNullOrWhiteSpace(this.AwsSecretKey);
            }
        }
        public void RefreshTerminatableInstances()
        {
            var manager = new Ec2Manager(this.AwsAccessKey, this.AwsSecretKey);
            this.TerminatableInstances = manager.ListInstances().Select(x => new LabelledValue(x.Item2, x.Item1)).ToArray();
            if (this.TerminatableInstances.Length == 0)
            {
                this.TerminatableInstances = new[] { new LabelledValue("No Running Instances", null) };
            }
            this.ActiveTerminatableInstance = this.TerminatableInstances[0];
        }

        public bool CanTerminateInstance
        {
            get
            {
                return !string.IsNullOrWhiteSpace(this.awsAccessKey) &&
                    !string.IsNullOrWhiteSpace(this.AwsSecretKey) &&
                    this.ActiveTerminatableInstance != null && !string.IsNullOrEmpty(this.ActiveTerminatableInstance.Value);
            }
        }
        public void TerminateInstance()
        {

        }
    }
}
