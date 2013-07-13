using Amazon;
using Amazon.EC2;
using Amazon.EC2.Model;
using Caliburn.Micro;
using Ec2Manager.Utilities;
using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Ec2Manager.Ec2Manager
{
    [Export]
    [PartCreationPolicy(CreationPolicy.Shared)]
    public class Ec2Connection : PropertyChangedBase
    {
        private Credentials credentials;
        private RegionEndpoint endpoint = RegionEndpoint.EUWest1;
        private AmazonEC2Client client;
        public ILogger Logger;

        public static readonly InstanceSize[] InstanceSizes = new[]
        {
            new InstanceSize("M1 Small", "m1.small"),
            new InstanceSize("M1 Medium", "m1.medium"),
            new InstanceSize("M1 Large", "m1.large"),
            new InstanceSize("M3 Extra Large", "m3.xlarge"),
            new InstanceSize("M3 2x Extra Large", "m3.2xlarge"),
            new InstanceSize("Micro", "t1.micro"),
            new InstanceSize("High-Memory Extra Large", "m2.xlarge"),
            new InstanceSize("High-Memory 2x Extra Large", "m2.2xlarge"),
            new InstanceSize("High-Memory 4x Extra Large", "m2.4xlarge"),
            new InstanceSize("High-CPU Medium", "c1.medium"),
            new InstanceSize("High-CPU Extra Large", "c1.xlarge"),
        };

        private bool isConnected;
        public bool IsConnected
        {
            get { return this.isConnected; }
            set
            {
                this.isConnected = value;
                this.NotifyOfPropertyChange();
            }
        }

        [ImportingConstructor]
        public Ec2Connection() : this(new StubLogger())
        {
        }

        public Ec2Connection(ILogger logger)
        {
            this.Logger = logger;
        }

        public void SetCredentials(Credentials credentials)
        {
            this.credentials = credentials;
            this.Connect();
        }

        public Ec2Instance CreateInstance(string name, string instanceAmi, InstanceSize instanceSize, string availabilityZone = null, double? spotBidPrice = null)
        {
            var instance = new Ec2Instance(this.client, name, new InstanceSpecification(instanceAmi, instanceSize, availabilityZone, spotBidPrice));
            instance.Logger = this.Logger;
            return instance;
        }

        //public Ec2Instance ReconnectInstance(string instanceId)
        //{
        //    var instance = new Ec2Instance(this.client, instanceId);
        //    instance.Logger = this.Logger;
        //    return instance;
        //}

        public async Task<double> GetCurrentSpotPriceAsync(InstanceSize instanceSize)
        {
            var result = await this.client.RequestAsync(s => s.DescribeSpotPriceHistory(new DescribeSpotPriceHistoryRequest()
            {
                InstanceType = new List<string>() { instanceSize.Key },
                ProductDescription = new List<string>() { "Linux/UNIX" },
                MaxResults = 1,
            }));

            return double.Parse(result.DescribeSpotPriceHistoryResult.SpotPriceHistory[0].SpotPrice);
        }

        public async Task<IEnumerable<Ec2Instance>> ListInstancesAsync()
        {
            var instances = (await this.client.RequestAsync(s => s.DescribeInstances(new DescribeInstancesRequest()
            {
                Filter = new List<Filter>()
                {
                    new Filter() { Name = "tag:CreatedByEc2Manager", Value = new List<string>() { "*" } },
                }
            }))).DescribeInstancesResult.Reservation.SelectMany(reservation => reservation.RunningInstance.Where(instance => instance.InstanceState.Name == "running"));

            return instances.Select(x => new Ec2Instance(this.client, x));
        }

        private void Connect()
        {
            if (!this.credentials.IsComplete)
                return;

            this.client = new AmazonEC2Client(this.credentials.AwsAccessKey, this.credentials.AwsSecretKey, this.endpoint);
            // TODO: Actually set properly
            this.IsConnected = true;
        }
    }
}
