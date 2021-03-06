﻿using Amazon;
using Amazon.EC2;
using Amazon.EC2.Model;
using Stylet;
using Ec2Manager.Configuration;
using Ec2Manager.Utilities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using StyletIoC;
using Amazon.IdentityManagement;
using System.Text.RegularExpressions;

namespace Ec2Manager.Ec2Manager
{
    public class Ec2Connection : PropertyChangedBase
    {
        public Credentials Credentials { get; private set; }
        public readonly RegionEndpoint Endpoint = RegionEndpoint.EUWest1;
        private AmazonEC2Client client;
        private string cachedUserId;
        private Config config;
        public ILogger Logger;

        public static readonly InstanceSize[] InstanceSizes = new[]
        {
            new InstanceSize("M3 Medium", "m3.medium"),
            new InstanceSize("M3 Large", "m3.large"),
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

        [Inject]
        public Ec2Connection(Config config) : this(new StubLogger(), config)
        {
        }

        public Ec2Connection(ILogger logger, Config config)
        {
            this.Logger = logger;
            this.config = config;
        }

        public void SetCredentials(Credentials credentials)
        {
            this.Credentials = credentials;
            this.Connect();
        }

        public Ec2Instance CreateInstance(string name, string instanceAmi, InstanceSize instanceSize, string availabilityZone = null, double? spotBidPrice = null)
        {
            var instance = new Ec2Instance(this.client, this.config, name, new InstanceSpecification(instanceAmi, instanceSize, availabilityZone, spotBidPrice));
            instance.Logger = this.Logger;
            return instance;
        }

        public Ec2SnapshotBrowser CreateSnapshotBrowser()
        {
            return new Ec2SnapshotBrowser(this.client);
        }

        public async Task<string> GetUserIdAsync()
        {
            // http://stackoverflow.com/a/18124234/1086121
            // Basically we have to try and retrieve our user ID. If we don't have permission, then the exception contains our user ID. YAY
            if (this.cachedUserId == null)
            {
                try
                {
                    this.cachedUserId = (await new Amazon.IdentityManagement.AmazonIdentityManagementServiceClient(Credentials.AwsAccessKey, Credentials.AwsSecretKey).GetUserAsync(new Amazon.IdentityManagement.Model.GetUserRequest())).User.UserId;
                }
                catch (AmazonIdentityManagementServiceException e)
                {
                    if (e.ErrorCode != "AccessDenied")
                        throw;
                    var match = Regex.Match(e.Message, "arn:aws:.*?([0-9]+)");
                    if (match.Groups.Count < 1)
                        throw;
                    this.cachedUserId = match.Groups[1].Value;
                }
            }
                
            return this.cachedUserId;
        }

        //public Ec2Instance ReconnectInstance(string instanceId)
        //{
        //    var instance = new Ec2Instance(this.client, instanceId);
        //    instance.Logger = this.Logger;
        //    return instance;
        //}

        public async Task<double> GetCurrentSpotPriceAsync(InstanceSize instanceSize)
        {
            var result = await this.client.DescribeSpotPriceHistoryAsync(new DescribeSpotPriceHistoryRequest()
            {
                InstanceTypes = new List<string>() { instanceSize.Key },
                ProductDescriptions = new List<string>() { "Linux/UNIX" },
                MaxResults = 1,
            });

            return double.Parse(result.SpotPriceHistory[0].Price);
        }

        public async Task<IEnumerable<Ec2Instance>> ListInstancesAsync()
        {
            var instances = (await this.client.DescribeInstancesAsync(new DescribeInstancesRequest()
            {
                Filters = new List<Filter>()
                {
                    new Filter() { Name = "tag:CreatedByEc2Manager", Values = new List<string>() { "*" } },
                }
            })).Reservations.SelectMany(reservation => reservation.Instances.Where(instance => instance.State.Name == "running"));

            return instances.Select(x => new Ec2Instance(this.client, this.config, x));
        }

        private void Connect()
        {
            if (!this.Credentials.IsComplete)
                return;

            this.client = new AmazonEC2Client(this.Credentials.AwsAccessKey, this.Credentials.AwsSecretKey, this.Endpoint);
            // TODO: Actually set properly
            this.IsConnected = true;
        }
    }
}
