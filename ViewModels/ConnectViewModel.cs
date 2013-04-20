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

        private IEventAggregator events;

        [ImportingConstructor]
        public ConnectViewModel(IEventAggregator events)
        {
            this.events = events;

            this.DisplayName = "Create New Instance";
            this.ActiveInstanceType = this.InstanceTypes[0];
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
            var manager = new Ec2Manager(this.AwsAccessKey, this.AwsSecretKey, this.InstanceName);
            events.Publish(new CreateInstanceEvent()
            {
                InstanceAmi = this.AMI,
                InstanceSize = this.ActiveInstanceType.Value,
                Manager = manager,
            });
        }
    }
}
