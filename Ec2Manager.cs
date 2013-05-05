using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Amazon.EC2;
using Amazon;
using Amazon.EC2.Model;
using Caliburn.Micro;
using System.IO;
using Ec2Manager.Classes;
using System.Threading;

namespace Ec2Manager
{
    public class Ec2Manager : PropertyChangedBase
    {
        private AmazonEC2Client client;
        private string uniqueKey;
        public string InstanceId { get; private set; }

        // We can mount on xvdf -> xvdp
        private char nextVolumeMountPointSuffix = 'f';
        private static readonly char maxVolumeMountPointSuffix = 'p';
        private object volumeMountPointLock = new object();

        public string PrivateKey { get; private set; }

        public string Name { get; set; }

        public ILogger DefaultLogger { get; set; }

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
        private string publicIp;
        public string PublicIp
        {
            get { return this.publicIp; }
            private set
            {
                this.publicIp = value;
                this.NotifyOfPropertyChange();
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

        /// <summary>
        /// Instantiate, with the intention of creating a new instance
        /// </summary>
        public Ec2Manager(string accessKey, string secretKey)
        {
            this.client = new AmazonEC2Client(accessKey, secretKey, RegionEndpoint.EUWest1);
            this.uniqueKey = Guid.NewGuid().ToString();
        }
        /// <summary>
        /// Associate with an already-running instance
        /// </summary>
        public Ec2Manager(string accessKey, string secretKey, string instanceId) : this(accessKey, secretKey)
        {
            this.InstanceId = instanceId;
            this.PublicIp = this.RetrieveAddress();
        }

        private RunningInstance GetRunningInstance()
        {
            bool worked = false;
            DescribeInstancesResponse describeInstancesResponse = null;

            var describeInstancesRequest = new DescribeInstancesRequest()
            {
                InstanceId = new List<string>() { this.InstanceId },
            };

            while (!worked)
            {  
                try
                {
                    describeInstancesResponse = this.client.DescribeInstances(describeInstancesRequest);
                    worked = true;
                }
                catch (AmazonEC2Exception e)
                {
                    if (e.ErrorCode != "InvalidInstanceID.NotFound")
                        throw;
                }
            }

            return describeInstancesResponse.DescribeInstancesResult.Reservation
                .SelectMany(x => x.RunningInstance)
                .Where(x => x.InstanceId == InstanceId)
                .FirstOrDefault();
        }

        private string GetNextDevice()
        {
            string result;
            lock (this.volumeMountPointLock)
            {
                if (this.nextVolumeMountPointSuffix > maxVolumeMountPointSuffix)
                    throw new Exception("Run out of mount points. You have too many volumes mounted!");

                result = "/dev/xvd" + this.nextVolumeMountPointSuffix;
                this.nextVolumeMountPointSuffix++;
            }
            return result;
        }

        private async Task UntilStateAsync(string state, CancellationToken? cancellationToken = null)
        {
            bool gotToState = false;

            while (!gotToState)
            {
                if (cancellationToken.HasValue && cancellationToken.Value.IsCancellationRequested)
                    break;

                this.InstanceState = this.GetRunningInstance().InstanceState.Name;

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

                if ((attachment.Count == 0 && allowNone) || attachment.FirstOrDefault(x => x.InstanceId == this.InstanceId).Status == state)
                {
                    gotToState = true;
                }
                else
                {
                    await Task.Delay(1000);
                }
            }
        }

        private void CreateSecurityGroup(string groupName, ILogger logger = null)
        {
            logger = logger ?? this.DefaultLogger;

            logger.Log("Creating a new security group: {0}", this.securityGroupName);
            var createSecurityGroupResponse = this.client.CreateSecurityGroup(new CreateSecurityGroupRequest()
            {
                GroupName = groupName,
                GroupDescription = "Ec2Manager-created security group",
            });
            logger.Log("Security group ID {0} created", createSecurityGroupResponse.CreateSecurityGroupResult.GroupId);
        }

        private void AuthorizeIngress(string groupName, IEnumerable<PortRangeDescription> portRanges, ILogger logger = null)
        {
            logger = logger ?? this.DefaultLogger;
            if (portRanges.Count() == 0)
                return;

            logger.Log("Allowing inbound access on {0}", string.Join(", ", portRanges));

            var ingressRequest = new AuthorizeSecurityGroupIngressRequest()
            {
                GroupName = groupName,
                IpPermissions = portRanges.Select(x => new IpPermissionSpecification()
                    {
                        IpProtocol = x.Proto,
                        FromPort = x.FromPort,
                        ToPort = x.ToPort,
                        IpRanges = new List<string>() { "0.0.0.0/0" },
                    }).ToList(),
            };

            try
            {
                this.client.AuthorizeSecurityGroupIngress(ingressRequest);
            }
            catch (AmazonEC2Exception e)
            {
                // Duplicate port settings are just fine
                if (e.ErrorCode != "InvalidPermission.Duplicate")
                    throw;
            }

            logger.Log("Inbound access authorised");
        }

        private string CreateKeyPair(string keyPairName, ILogger logger = null)
        {
            logger = logger ?? this.DefaultLogger;

            logger.Log("Creating a new key pair: {0}", this.keyPairName);
            var newKeyResponse = this.client.CreateKeyPair(new CreateKeyPairRequest()
            {
                KeyName = keyPairName,
            });
            var keyPair = newKeyResponse.CreateKeyPairResult.KeyPair;
            logger.Log("Key pair created. Fingerprint {0}", keyPair.KeyFingerprint);

            return keyPair.KeyMaterial;
        }

        private async Task CreateInstanceAsync(string ami, string size, string keyPairName, string securityGroup, string name, string availabilityZone = null, ILogger logger = null)
        {
            logger = logger ?? this.DefaultLogger;

            logger.Log("Creating a new instance. AMI: {0}, size: {1}", ami, size);
            var runInstanceRequest = new RunInstancesRequest()
            {
                ImageId = ami,
                InstanceType = size,
                MinCount = 1,
                MaxCount = 1,
                KeyName = keyPairName,
                SecurityGroup = new List<string>() { securityGroup },
            };
            if (!string.IsNullOrWhiteSpace(availabilityZone))
            {
                runInstanceRequest.Placement = new Placement() { AvailabilityZone = availabilityZone };
            }

            var runResponse = this.client.RunInstances(runInstanceRequest);
            var instances = runResponse.RunInstancesResult.Reservation.RunningInstance;
            this.InstanceId = instances[0].InstanceId;
            logger.Log("New instance created. Instance ID: {0}", this.InstanceId);

            // Tag straight away. They might get bored and close the window while it's launching
            logger.Log("Tagging instance");
            this.client.CreateTags(new CreateTagsRequest()
            {
                ResourceId = new List<string>() { this.InstanceId },
                Tag = new List<Tag>()
                {
                    new Tag() { Key = "CreatedByEc2Manager", Value = "true" },
                    new Tag() { Key = "Name", Value = name },
                },
            });

            logger.Log("Waiting for instance to reach 'running' state");
            // Sometimes (I have no idea why) AWS reports the instance as pending when the console shows it running
            // The observed fix is to restart it
            CancellationTokenSource cts = new CancellationTokenSource(TimeSpan.FromSeconds(45));
            await this.UntilStateAsync("running", cts.Token);
            if (cts.IsCancellationRequested)
            {
                logger.Log("The instance is taking a long time to come up. This happens sometimes.");
                logger.Log("Sometimes issuing a reboot fixes it, so trying that...");
                this.client.RebootInstances(new RebootInstancesRequest() 
                {
                    InstanceId = new List<string>() { this.InstanceId },
                });
                await this.UntilStateAsync("running");
            }

            logger.Log("Instance is now running");
        }

        private string AllocateAddress(ILogger logger = null)
        {
            logger = logger ?? this.DefaultLogger;

            logger.Log("Allocating an IP address");
            var allocateResponse = this.client.AllocateAddress(new AllocateAddressRequest());
            var publicIp = allocateResponse.AllocateAddressResult.PublicIp;
            logger.Log("Ip address {0} allocated", publicIp);

            return publicIp;
        }

        private string RetrieveAddress(ILogger logger = null)
        {
            logger = logger ?? this.DefaultLogger;

            logger.Log("Retriving public IP of instance");
            return this.GetRunningInstance().IpAddress;
        }

        private void AssignAddress(string publicIp, ILogger logger = null)
        {
            logger = logger ?? this.DefaultLogger;

            logger.Log("Assigning public IP {0} to instance", this.PublicIp);
            this.client.AssociateAddress(new AssociateAddressRequest()
            {
                InstanceId = this.InstanceId,
                PublicIp = publicIp,
            });
            logger.Log("Public IP assigned");
        }

        private void ReleaseIp(string publicIp, ILogger logger = null)
        {
            logger = logger ?? this.DefaultLogger;

            logger.Log("Releasing IP address {0}", publicIp);
            this.client.DisassociateAddress(new DisassociateAddressRequest()
            {
                PublicIp = publicIp,
            });
            this.client.ReleaseAddress(new ReleaseAddressRequest()
            {
                PublicIp = publicIp,
            });
            logger.Log("Ip address released");
        }

        private async Task DeleteVolumeAsync(string volumeId, ILogger logger = null)
        {
            logger = logger ?? this.DefaultLogger;

            logger.Log("Detaching volume {0}", volumeId);
            this.client.DetachVolume(new DetachVolumeRequest()
            {
                Force = true,
                InstanceId = this.InstanceId,
                VolumeId = volumeId,
            });

            logger.Log("Waiting until volume {0} becomes available", volumeId);
            await this.UntilVolumeAttachedStateAsync(volumeId, "available");

            logger.Log("Deleting volume {0}", volumeId);
            this.client.DeleteVolume(new DeleteVolumeRequest()
            {
                VolumeId = volumeId,
            });

            logger.Log("Volume {0} deleted", volumeId);
        }

        private async Task TerminateInstanceAsync( ILogger logger = null)
        {
            logger = logger ?? this.DefaultLogger;

            logger.Log("Terminating instance");
            this.client.TerminateInstances(new TerminateInstancesRequest()
            {
                InstanceId = new List<string>() { this.InstanceId },
            });

            logger.Log("Waiting for instance to reach the 'terminated' state");
            await this.UntilStateAsync("terminated");
            logger.Log("Instance terminated");
        }

        private void DeleteSecurityGroup(string groupId, ILogger logger = null)
        {
            logger = logger ?? this.DefaultLogger;

            logger.Log("Deleting security group {0}", groupId);
            try
            {
                this.client.DeleteSecurityGroup(new DeleteSecurityGroupRequest()
                {
                    GroupId = groupId,
                });
            }
            catch (AmazonEC2Exception e)
            {
                // It can take a long while for groups to appear, as far as this command is concerned.
                // If this happens, ignore it.
                if (e.ErrorCode == "InvalidGroup.NotFound")
                    logger.Log("Failed to delete security group as Ec2 doesn't think it exists (this happens...)");
                else
                    throw;
            }
        }

        private void DeleteKeyPair(string keyName, ILogger logger = null)
        {
            logger = logger ?? this.DefaultLogger;

            logger.Log("Deleting key pair: {0}", keyName);
            this.client.DeleteKeyPair(new DeleteKeyPairRequest()
            {
                KeyName = keyName,
            });
        }

        private async Task AttachVolumeAsync(string volumeId, string device, ILogger logger = null)
        {
            logger = logger ?? this.DefaultLogger;

            logger.Log("Attaching volume to instance {0}, device {1}", this.InstanceId, device);
            var attachVolumeResponse = this.client.AttachVolume(new AttachVolumeRequest()
            {
                InstanceId = this.InstanceId,
                VolumeId = volumeId,
                Device = device,
            });

            logger.Log("Waiting for volume to reach the 'attached' state");
            await this.UntilVolumeAttachedStateAsync(volumeId, "attached");
        }

        public async Task CreateAsync(string instanceAmi, string instanceSize, string availabilityZone = null, ILogger logger = null)
        {
            logger = logger ?? this.DefaultLogger;

            logger.Log("Starting instance creation process");

            try
            {
                this.CreateSecurityGroup(this.securityGroupName, logger);
            }
            catch (AmazonEC2Exception e)
            {
                logger.Log("Error creating security group: {0}", e.Message);
                throw;
            }

            try
            {
                this.AuthorizeIngress(this.securityGroupName, new[] { new PortRangeDescription(22, 22, "tcp"), new PortRangeDescription(-1, -1, "icmp") }, logger);
            }
            catch (AmazonEC2Exception e)
            {
                logger.Log("Error authorising ingress: {0}. Performing rollback", e.Message);
                this.DeleteSecurityGroup(this.securityGroupName, logger);
                throw;
            }

            try
            {
                this.PrivateKey = this.CreateKeyPair(this.keyPairName, logger);
            }
            catch (AmazonEC2Exception e)
            {
                logger.Log("Error creating key pair: {0}. Performing rollback", e.Message);
                this.DeleteSecurityGroup(this.securityGroupName, logger);
                throw;
            }

            try
            {
                await this.CreateInstanceAsync(instanceAmi, instanceSize, this.keyPairName, this.securityGroupName, this.Name, availabilityZone, logger);
            }
            catch (AmazonEC2Exception e)
            {
                logger.Log("Error creating instance: {0}. Performing rollback", e.Message);
                this.DeleteKeyPair(this.keyPairName, logger);
                this.DeleteSecurityGroup(this.securityGroupName, logger);
                throw;
            }

            AmazonEC2Exception exception = null;
            try
            {
                this.PublicIp = this.AllocateAddress(logger);
            }
            catch (AmazonEC2Exception e)
            {
                exception = e;
            }
            if (exception != null)
            {
                logger.Log("Error allocating public IP: {0}. Performing rollback", exception.Message);
                await this.TerminateInstanceAsync(logger);
                this.DeleteKeyPair(this.keyPairName, logger);
                this.DeleteSecurityGroup(this.securityGroupName, logger);
                throw exception;
            }

            exception = null;
            try
            {
                this.AssignAddress(this.PublicIp, logger);
            }
            catch (AmazonEC2Exception e)
            {
                exception = e;
            }
            if (exception != null)
            {
                logger.Log("Error allocating public IP: {0}. Performing rollback", exception.Message);
                this.ReleaseIp(this.PublicIp, logger);
                await this.TerminateInstanceAsync(logger);
                this.DeleteKeyPair(this.keyPairName, logger);
                this.DeleteSecurityGroup(this.securityGroupName, logger);
                throw exception;
            }

            logger.Log("Instance has been created");
        }

        public async Task<string> MountVolumeAsync(string volumeId, IMachineInteractionProvider client, string name = null, ILogger logger = null)
        {
            logger = logger ?? this.DefaultLogger;
            bool weCreatedVolume = false;

            if (volumeId.StartsWith("snap-"))
            {
                weCreatedVolume = true;
                try
                {
                    volumeId = await this.CreateVolumeFromSnapshot(volumeId, client, name, logger);
                }
                catch (AmazonEC2Exception e)
                {
                    logger.Log("Error creating volume from snapshot: {0}", e.Message);
                    throw;
                }
            }
            else if (!volumeId.StartsWith("vol-"))
                throw new Exception("Volume ID must start with vol- or snap-");

            string device = null;
            string deviceMountPoint = null;
            AmazonEC2Exception exception = null;
            try
            {
                device = this.GetNextDevice();
                deviceMountPoint = Path.GetFileName(device);
                await this.AttachVolumeAsync(volumeId, device, logger);

                logger.Log("Mounting and setting up device");
                await client.MountAndSetupDeviceAsync(device, deviceMountPoint, logger);

                logger.Log("Retriving port settings");
                var portSettings = client.GetPortDescriptions(deviceMountPoint, logger).ToArray();

                this.AuthorizeIngress(this.securityGroupName, portSettings, logger);
            }
            catch (AmazonEC2Exception e)
            {
                exception = e;
            }
            if (exception != null)
            {
                logger.Log("Error performing the last operation: {0}. Rolling back", exception.Message);
                if (weCreatedVolume)
                    await this.DeleteVolumeAsync(volumeId, logger);
                throw exception;
            }

            logger.Log("Volume successfully mounted");

            return deviceMountPoint;
        }

        public async Task<string> CreateVolumeFromSnapshot(string snapshotId, IMachineInteractionProvider client, string name = null, ILogger logger = null)
        {
            logger = logger ?? this.DefaultLogger;

            logger.Log("Starting device mount process. Snapshot ID: {0}", snapshotId);

            logger.Log("Creating EBS volume based on snapshot");
            var createVolumeResponse = this.client.CreateVolume(new CreateVolumeRequest()
            {
                SnapshotId = snapshotId,
                VolumeType = "standard",
                AvailabilityZone = this.GetRunningInstance().Placement.AvailabilityZone,
            });
            var volumeId = createVolumeResponse.CreateVolumeResult.Volume.VolumeId;
            logger.Log("Volume ID {0} created", volumeId);

            logger.Log("Tagging volume, so we know we can remove it later");
            this.client.CreateTags(new CreateTagsRequest()
            {
                ResourceId = new List<string>() { volumeId },
                Tag = new List<Tag>()
                {
                    new Tag() { Key = "CreatedByEc2Manager", Value = "true" },
                    new Tag() { Key = "Name", Value = name ?? "Unnamed" },
                },
            });

            logger.Log("Waiting for volume to reach the 'available' state");
            await this.UntilVolumeStateAsync(volumeId, "available");

            return volumeId;
        }

        public IEnumerable<Tuple<string, string>> ListInstances()
        {
            var instances = this.client.DescribeInstances(new DescribeInstancesRequest()).DescribeInstancesResult.Reservation
                .SelectMany(reservation => reservation.RunningInstance.Where(instance => instance.InstanceState.Name == "running" && instance.Tag.Any(tag => tag.Key == "CreatedByEc2Manager")));

           return instances.Select(x => new Tuple<string, string>(x.InstanceId, (x.Tag.FirstOrDefault(tag => tag.Key == "Name") ?? new Tag() { Value = "No Name" }).Value));
        }

        public async Task DestroyAsync(ILogger logger = null)
        {
            logger = logger ?? this.DefaultLogger;

            logger.Log("Starting instance termination process");

            var instanceStatus = this.GetRunningInstance();
            var groupIds = instanceStatus.GroupId;
            var keyName = instanceStatus.KeyName;
            // This excludes volumes attached to other machines as well
            var volumes = this.client.DescribeVolumes(new DescribeVolumesRequest()).DescribeVolumesResult.Volume
                .Where(x => x.Attachment.Count == 1 && x.Attachment[0].InstanceId == this.InstanceId)
                .Where(x => x.Tag.Any(y => y.Key == "CreatedByEc2Manager"))
                .Select(x => x.VolumeId);


            // Public IPs are a limited resource and people are impatient. Make sure we release
            // the IP before they get too bored
            if (this.PublicIp != null)
            {
                this.ReleaseIp(this.PublicIp, logger);
            }

            // Detach the volumes in parallel, since it takes a nice long time
            logger.Log("Found uniquely attached volumes: {0}", string.Join(", ", volumes));
            await Task.WhenAll(volumes.Select(volume => this.DeleteVolumeAsync(volume, logger)));

            await this.TerminateInstanceAsync(logger);

            // This has to be set after the instance has been terminated
            var allInstances = this.client.DescribeInstances(new DescribeInstancesRequest()).DescribeInstancesResult.Reservation
                .Where(x => x.RunningInstance.All(y => y.InstanceState.Name != "terminated")).ToArray();

            var usedGroupIds = allInstances.SelectMany(x => x.GroupId).Distinct();
            logger.Log("Found security groups uniquely associated with instance: {0}", string.Join(", ", groupIds.Except(usedGroupIds)));

            foreach (var groupId in groupIds.Except(usedGroupIds))
            {
                this.DeleteSecurityGroup(groupId, logger);
            }

            var usedKeyNames = allInstances.SelectMany(x => x.RunningInstance.Select(y => y.KeyName)).Distinct();
            if (!usedKeyNames.Contains(keyName))
            {
                this.DeleteKeyPair(keyName, logger);
            }

            logger.Log("Instance successfully terminated");
        }
    }
}
