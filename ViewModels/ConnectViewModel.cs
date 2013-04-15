using Caliburn.Micro;
using Ec2Manager.Classes;
using Ec2Manager.Properties;
using PropertyChanged;
using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Ec2Manager.ViewModels
{
    [ImplementPropertyChanged]
    [Export]
    class ConnectViewModel : Screen
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

        public string AwsAccessKey { get; set; }
        public string AwsSecretKey { get; set; }

        public string InstanceName { get; set; }
        public LabelledValue[] InstanceTypes
        {
            get { return instanceTypes; }
        }
        public LabelledValue ActiveInstanceType { get; set; }

        public string AMI { get; set; }

        [ImportingConstructor]
        public ConnectViewModel()
        {
            this.DisplayName = "Create New Instance";
            this.ActiveInstanceType = this.InstanceTypes[0];
            this.AMI = Settings.Default.DefaultAMI;
        }


        public bool CanCreate
        {
            get
            {
                return true;
                return !string.IsNullOrWhiteSpace(this.AwsAccessKey) &&
                    !string.IsNullOrWhiteSpace(this.AwsSecretKey) &&
                    !string.IsNullOrWhiteSpace(this.InstanceName) &&
                    !string.IsNullOrWhiteSpace(this.AMI);
            }
        }
        public void Create()
        {

        }
    }
}
