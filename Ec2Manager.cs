﻿using System;
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
    public class Ec2Manager : PropertyChangedBase
    {
        private AmazonEC2Client client;
        private string uniqueKey;
        private string reservationId;
        private string instanceId;

        // We can mount on xvdf -> xvdp
        private char nextVolumeMountPointSuffix = 'f';
        private static readonly char maxVolumeMountPointSuffix = 'p';

        public string PrivateKey { get; private set; }
        public string PublicIp { get; private set; }
        public string Name { get; set; }

        public Logger Logger { get; set; }

        private string instanceState;
        public string InstanceState
        {
            get { return this.instanceState; }
            private set
            {
                this.instanceState = value;
                NotifyOfPropertyChange(() => InstanceState);
            }
        }

        private string securityGroupName
        {
            get { return "Ec2SecurityGroup-" + this.uniqueKey; }
        }

        private string keyPairName
        {
            get { return "Ec2KeyPair-" + this.uniqueKey; }
        }

        public Ec2Manager(string accessKey, string secretKey, string name)
        {
            this.Name = name;

            this.client = new AmazonEC2Client(accessKey, secretKey, RegionEndpoint.EUWest1);
            this.uniqueKey = Guid.NewGuid().ToString();
        }

        private RunningInstance GetRunningInstance()
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

            return describeInstancesResponse.DescribeInstancesResult.Reservation
                .Where(x => x.ReservationId == this.reservationId)
                .FirstOrDefault().RunningInstance
                .Where(x => x.InstanceId == this.instanceId)
                .FirstOrDefault();
        }

        private string GetNextDevice()
        {
            if (this.nextVolumeMountPointSuffix > maxVolumeMountPointSuffix)
                throw new Exception("Run out of mount points. You have too many volumes mounted!");

            var result = "/dev/xvd" + this.nextVolumeMountPointSuffix;
            this.nextVolumeMountPointSuffix++;
            return result;
        }

        private void Log(string text, params string[] parameters)
        {
            var logger = this.Logger;
            if (logger != null)
                logger.Log(text, parameters);
        }

        public async Task CreateAsync(string instanceAmi, string instanceSize)
        {
            this.Log("Starting instance creation process");

            this.Log("Allocating an IP address");
            var allocateResponse = this.client.AllocateAddress(new AllocateAddressRequest());
            this.PublicIp = allocateResponse.AllocateAddressResult.PublicIp;
            this.Log("Ip address {0} allocated", this.PublicIp);

            this.Log("Creating a new security group: {0}", this.securityGroupName);
            var createSecurityGroupResponse = this.client.CreateSecurityGroup(new CreateSecurityGroupRequest()
            {
                GroupName = this.securityGroupName,
                GroupDescription = "Ec2Manager-created security group",
            });
            this.Log("Security group ID {0} created", createSecurityGroupResponse.CreateSecurityGroupResult.GroupId);

            this.Log("Allowing inbound access on 22/tcp (SSH)");
            var ipPermission = new IpPermissionSpecification()
            {
                IpProtocol = "tcp",
                FromPort = 22,
                ToPort = 22,
            };
            ipPermission.IpRanges.Add("0.0.0.0/0");

            var ingressRequest = new AuthorizeSecurityGroupIngressRequest()
            {
                GroupName = this.securityGroupName,
            };
            ingressRequest.IpPermissions.Add(ipPermission);

            this.client.AuthorizeSecurityGroupIngress(ingressRequest);
            this.Log("Inbound access authorised");

            this.Log("Creating a new key pair: {0}", this.keyPairName);
            var newKeyResponse = this.client.CreateKeyPair(new CreateKeyPairRequest()
            {
                KeyName = this.keyPairName,
            });
            var keyPair = newKeyResponse.CreateKeyPairResult.KeyPair;
            this.PrivateKey = keyPair.KeyMaterial;
            this.Log("Key pair created. Fingerprint {0}", keyPair.KeyFingerprint);

            this.Log("Creating a new instance. AMI: {0}, size: {1}", instanceAmi, instanceSize);
            var runInstanceRequest = new RunInstancesRequest()
            {
                ImageId = instanceAmi,
                InstanceType = instanceSize,
                MinCount = 1,
                MaxCount = 1,
                KeyName = this.keyPairName,
            };
            runInstanceRequest.SecurityGroup.Add(this.securityGroupName);

            var runResponse = this.client.RunInstances(runInstanceRequest);
            this.reservationId = runResponse.RunInstancesResult.Reservation.ReservationId;
            var instances = runResponse.RunInstancesResult.Reservation.RunningInstance;
            this.instanceId = instances[0].InstanceId;
            this.Log("New instance created. Reservation ID: {0}, Instance ID: {1}", this.reservationId, this.instanceId);

            this.Log("Waiting for instance to reach 'running' state");
            await this.UntilStateAsync("running");
            this.Log("Instance is now running");

            this.Log("Assigning public IP {0} to instance", this.PublicIp);
            this.client.AssociateAddress(new AssociateAddressRequest() 
            {
                InstanceId = this.instanceId,
                PublicIp = this.PublicIp,
            });
            this.Log("Public IP assigned");

            this.Log("Instance is now ready for use");
        }

        public async Task MountDevice(string snapshotId, string mountPoint, IMachineInteractionProvider client)
        {
            var createVolumeResponse = this.client.CreateVolume(new CreateVolumeRequest()
            {
                SnapshotId = snapshotId,
                VolumeType = "standard",
                AvailabilityZone = this.GetRunningInstance().Placement.AvailabilityZone,
            });
            var volumeId = createVolumeResponse.CreateVolumeResult.Volume.VolumeId;

            await this.UntilVolumeStateAsync(volumeId, "available");

            var device = this.GetNextDevice();
            var attachVolumeResponse = this.client.AttachVolume(new AttachVolumeRequest()
            {
                InstanceId = this.instanceId,
                VolumeId = volumeId,
                Device = device,
            });

            await this.UntilVolumeAttachedStateAsync(volumeId, "attached");

            client.MountAndSetupDevice(device, mountPoint);
        }

        private async Task UntilStateAsync(string state)
        {
            bool gotToState = false;

            while (!gotToState)
            {
                this.instanceState = this.GetRunningInstance().InstanceState.Name;

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

        private async Task UntilVolumeStateAsync(string volumeId, string state)
        {
            bool gotToState = false;

            var describeVolumesRequest = new DescribeVolumesRequest()
            {
                VolumeId = new List<string>() { volumeId },
            };

            while (!gotToState)
            {
                var describeVolumesResponse = this.client.DescribeVolumes(describeVolumesRequest);
                var status = describeVolumesResponse.DescribeVolumesResult.Volume.FirstOrDefault(x => x.VolumeId == volumeId).Status;

                if (status == state)
                {
                    gotToState = true;
                }
                else
                {
                    await Task.Delay(1000);
                }
            }
        }

        private async Task UntilVolumeAttachedStateAsync(string volumeId, string state, bool allowNone = true)
        {
            bool gotToState = false;

            var describeVolumesRequest = new DescribeVolumesRequest()
            {
                VolumeId = new List<string>() { volumeId },
            };

            while (!gotToState)
            {
                var describeVolumesResponse = this.client.DescribeVolumes(describeVolumesRequest);
                var attachment = describeVolumesResponse.DescribeVolumesResult.Volume
                    .FirstOrDefault(x => x.VolumeId == volumeId)
                    .Attachment;

                if ((attachment.Count == 0 && allowNone) || attachment.FirstOrDefault(x => x.InstanceId == this.instanceId).Status == state)
                {
                    gotToState = true;
                }
                else
                {
                    await Task.Delay(1000);
                }
            }
        }

        public async Task DestroyAsync()
        {
            var instanceStatus = this.GetRunningInstance();
            var groupIds = instanceStatus.GroupId;
            var keyName = instanceStatus.KeyName;
            // This excludes volumes attached to other machines as well
            var volumes = this.client.DescribeVolumes(new DescribeVolumesRequest()).DescribeVolumesResult.Volume
                .Where(x => x.Attachment.Count == 1 && x.Attachment[0].InstanceId == this.instanceId)
                .Select(x => x.VolumeId);

            foreach (var volume in volumes)
            {
                this.client.DetachVolume(new DetachVolumeRequest()
                {
                    Force = true,
                    InstanceId = this.instanceId,
                    VolumeId = volume,
                });

                await this.UntilVolumeAttachedStateAsync(volume, "available");

                this.client.DeleteVolume(new DeleteVolumeRequest()
                {
                    VolumeId = volume,
                });
            }

            var termResponse = this.client.TerminateInstances(new TerminateInstancesRequest()
            {
                InstanceId = new List<string>() { this.instanceId },
            });

            await this.UntilStateAsync("terminated");

            // This has to be set after the instance has been terminated
            var allInstances = this.client.DescribeInstances(new DescribeInstancesRequest()).DescribeInstancesResult.Reservation
                .Where(x => x.RunningInstance.All(y => y.InstanceState.Name != "terminated")).ToArray();

            var usedGroupIds = allInstances.SelectMany(x => x.GroupId).Distinct();
            foreach (var groupId in groupIds.Except(usedGroupIds))
            {
                this.client.DeleteSecurityGroup(new DeleteSecurityGroupRequest()
                {
                    GroupId = groupId,
                });
            }

            var usedKeyNames = allInstances.SelectMany(x => x.RunningInstance.Select(y => y.KeyName)).Distinct();
            if (!usedKeyNames.Contains(keyName))
            {
                this.client.DeleteKeyPair(new DeleteKeyPairRequest()
                {
                    KeyName = keyName,
                });
            }

            this.client.ReleaseAddress(new ReleaseAddressRequest()
            {
                PublicIp = this.PublicIp,
            });
        }
    }
}
