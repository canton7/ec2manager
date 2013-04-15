using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Amazon.EC2;
using Amazon;
using Amazon.EC2.Model;
using Caliburn.Micro;

namespace Ec2Manager
{
    class Ec2Manager : PropertyChangedBase
    {
        private AmazonEC2Client client;
        private string uniqueKey;
        private string reservationId;
        private string instanceId;

        public string PrivateKey;
        public string PublicIp;

        private string instanceState;
        public string InstanceState
        {
            get { return this.instanceState; }
            set
            {
                this.instanceState = value;
                NotifyOfPropertyChange(() => InstanceState);
            }
        }

        public Ec2Manager(string accessKey, string secretKey)
        {
            this.client = new AmazonEC2Client(accessKey, secretKey, RegionEndpoint.EUWest1);
            this.uniqueKey = Guid.NewGuid().ToString();
        }

        public async Task CreateAsync()
        {
            var newGroupRequest = new CreateSecurityGroupRequest()
            {
                GroupName = "Ec2SecurityGroup-" + this.uniqueKey,
                GroupDescription = "Security groupf or HLDS stuff",
            };

            this.client.CreateSecurityGroup(newGroupRequest);

            var ipPermission = new IpPermissionSpecification()
            {
                IpProtocol = "tcp",
                FromPort = 22,
                ToPort = 22,
            };
            ipPermission.IpRanges.Add("0.0.0.0/0");

            var ingressRequest = new AuthorizeSecurityGroupIngressRequest()
            {
                GroupName = "Ec2SecurityGroup-" + this.uniqueKey,
            };
            ingressRequest.IpPermissions.Add(ipPermission);

            this.client.AuthorizeSecurityGroupIngress(ingressRequest);

            var newKeyRequest = new CreateKeyPairRequest()
            {
                KeyName = "Ec2KeyPair-" + this.uniqueKey,
            };

            var newKeyResponse = this.client.CreateKeyPair(newKeyRequest);
            var keyPair = newKeyResponse.CreateKeyPairResult.KeyPair;
            this.PrivateKey = keyPair.KeyMaterial;

            var runInstanceRequest = new RunInstancesRequest()
            {
                ImageId = "ami-8ec9dcfa",
                InstanceType = "m1.small",
                MinCount = 1,
                MaxCount = 1,
                KeyName = "Ec2KeyPair-" + this.uniqueKey,
            };
            runInstanceRequest.SecurityGroup.Add("Ec2SecurityGroup-" + this.uniqueKey);

            var runResponse = this.client.RunInstances(runInstanceRequest);
            this.reservationId = runResponse.RunInstancesResult.Reservation.ReservationId;
            var instances = runResponse.RunInstancesResult.Reservation.RunningInstance;
            this.instanceId = instances[0].InstanceId;

            await this.UntilStateAsync("running");

            var allocateResponse = this.client.AllocateAddress(new AllocateAddressRequest());
            this.PublicIp = allocateResponse.AllocateAddressResult.PublicIp;

            this.client.AssociateAddress(new AssociateAddressRequest() 
            {
                InstanceId = this.instanceId,
                PublicIp = this.PublicIp,
            });

        }

        private async Task UntilStateAsync(string state)
        {
            bool gotToState = false;

            while (!gotToState)
            {
                var describeInstancesRequest = new DescribeInstancesRequest()
                {
                    InstanceId = new List<string>(){ this.instanceId },
                };
                DescribeInstancesResponse describeInstancesResponse = null;

                try
                {
                    describeInstancesResponse = this.client.DescribeInstances(describeInstancesRequest);
                }
                catch (AmazonEC2Exception e)
                {
                }

                this.InstanceState = describeInstancesResponse.DescribeInstancesResult.Reservation
                    .Where(x => x.ReservationId == this.reservationId)
                    .FirstOrDefault().RunningInstance
                    .Where(x => x.InstanceId == this.instanceId)
                    .FirstOrDefault()
                    .InstanceState.Name;

                if (this.InstanceState == state)
                {
                    gotToState = true;
                }
                else
                {
                    await Task.Delay(2000);
                }
            }
        }

        public async Task DestroyAsync()
        {
            var termRequest = new TerminateInstancesRequest();
            termRequest.InstanceId = new List<string>() { this.instanceId };
            var termResponse = this.client.TerminateInstances(termRequest);

            await this.UntilStateAsync("terminated");

            var deleteSecurityGroupRequest = new DeleteSecurityGroupRequest()
            {
                GroupName = "Ec2SecurityGroup-" + this.uniqueKey,
            };
            this.client.DeleteSecurityGroup(deleteSecurityGroupRequest);

            var deleteKeyPairRequest = new DeleteKeyPairRequest()
            {
                KeyName = "Ec2KeyPair-" + this.uniqueKey,
            };
            this.client.DeleteKeyPair(deleteKeyPairRequest);

            this.client.ReleaseAddress(new ReleaseAddressRequest()
            {
                PublicIp = this.PublicIp,
            });
        }
    }
}
