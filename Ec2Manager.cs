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
        private object volumeMountPointLock = new object();

        public string PrivateKey { get; private set; }
        public string PublicIp { get; private set; }
        public string Name { get; set; }

        public Logger DefaultLogger { get; set; }

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

        public Ec2Manager(string accessKey, string secretKey)
        {
            this.client = new AmazonEC2Client(accessKey, secretKey, RegionEndpoint.EUWest1);
            this.uniqueKey = Guid.NewGuid().ToString();
        }

        public Ec2Manager(string accessKey, string secretKey, string instanceId) : this(accessKey, secretKey)
        {
            this.instanceId = instanceId;
        }

        private RunningInstance GetRunningInstance()
        {
            bool worked = false;
            DescribeInstancesResponse describeInstancesResponse = null;

            var describeInstancesRequest = new DescribeInstancesRequest()
            {
                InstanceId = new List<string>() { this.instanceId },
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
                .Where(x => x.ReservationId == this.reservationId)
                .FirstOrDefault().RunningInstance
                .Where(x => x.InstanceId == this.instanceId)
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

        public async Task CreateAsync(string instanceAmi, string instanceSize, string availabilityZone = null, Logger logger = null)
        {
            logger = logger ?? this.DefaultLogger;

            logger.Log("Starting instance creation process");

            logger.Log("Allocating an IP address");
            var allocateResponse = this.client.AllocateAddress(new AllocateAddressRequest());
            this.PublicIp = allocateResponse.AllocateAddressResult.PublicIp;
            logger.Log("Ip address {0} allocated", this.PublicIp);

            logger.Log("Creating a new security group: {0}", this.securityGroupName);
            var createSecurityGroupResponse = this.client.CreateSecurityGroup(new CreateSecurityGroupRequest()
            {
                GroupName = this.securityGroupName,
                GroupDescription = "Ec2Manager-created security group",
            });
            logger.Log("Security group ID {0} created", createSecurityGroupResponse.CreateSecurityGroupResult.GroupId);

            logger.Log("Allowing inbound access on 22/tcp (SSH) and ping");
            var ingressRequest = new AuthorizeSecurityGroupIngressRequest()
            {
                GroupName = this.securityGroupName,
                IpPermissions = new List<IpPermissionSpecification>()
                {
                    new IpPermissionSpecification()
                    {
                        IpProtocol = "tcp",
                        FromPort = 22,
                        ToPort = 22,
                        IpRanges = new List<string>() { "0.0.0.0/0" },
                    },
                    new IpPermissionSpecification()
                    {
                        IpProtocol = "icmp",
                        FromPort = -1,
                        ToPort = -1,
                        IpRanges = new List<string>() { "0.0.0.0/0" },
                    },
                },
            };

            this.client.AuthorizeSecurityGroupIngress(ingressRequest);
            logger.Log("Inbound access authorised");

            logger.Log("Creating a new key pair: {0}", this.keyPairName);
            var newKeyResponse = this.client.CreateKeyPair(new CreateKeyPairRequest()
            {
                KeyName = this.keyPairName,
            });
            var keyPair = newKeyResponse.CreateKeyPairResult.KeyPair;
            this.PrivateKey = keyPair.KeyMaterial;
            logger.Log("Key pair created. Fingerprint {0}", keyPair.KeyFingerprint);

            logger.Log("Creating a new instance. AMI: {0}, size: {1}", instanceAmi, instanceSize);
            var runInstanceRequest = new RunInstancesRequest()
            {
                ImageId = instanceAmi,
                InstanceType = instanceSize,
                MinCount = 1,
                MaxCount = 1,
                KeyName = this.keyPairName,
            };
            runInstanceRequest.SecurityGroup.Add(this.securityGroupName);
            if (!string.IsNullOrWhiteSpace(availabilityZone))
            {
                runInstanceRequest.Placement = new Placement()
                {
                    AvailabilityZone = availabilityZone,
                };
            }

            var runResponse = this.client.RunInstances(runInstanceRequest);
            this.reservationId = runResponse.RunInstancesResult.Reservation.ReservationId;
            var instances = runResponse.RunInstancesResult.Reservation.RunningInstance;
            this.instanceId = instances[0].InstanceId;
            logger.Log("New instance created. Reservation ID: {0}, Instance ID: {1}", this.reservationId, this.instanceId);

            logger.Log("Tagging instance");
            this.client.CreateTags(new CreateTagsRequest()
            {
                ResourceId = new List<string>() { this.instanceId },
                Tag = new List<Tag>()
                {
                    new Tag() { Key = "CreatedByEc2Manager", Value = "true" },
                    new Tag() { Key = "Name", Value = this.Name },
                },
            });

            logger.Log("Waiting for instance to reach 'running' state");
            await this.UntilStateAsync("running");
            logger.Log("Instance is now running");

            logger.Log("Assigning public IP {0} to instance", this.PublicIp);
            this.client.AssociateAddress(new AssociateAddressRequest() 
            {
                InstanceId = this.instanceId,
                PublicIp = this.PublicIp,
            });
            logger.Log("Public IP assigned");

            logger.Log("Instance has been created");
        }

        public async Task<string> MountVolumeAsync(string volumeId, IMachineInteractionProvider client, Logger logger = null)
        {
            logger = logger ?? this.DefaultLogger;

            if (volumeId.StartsWith("snap-"))
                volumeId = await this.CreateVolumeFromSnapshot(volumeId, client, logger);
            else if (!volumeId.StartsWith("vol-"))
                throw new Exception("Volume ID must start with vol- or snap-");

            var device = this.GetNextDevice();
            var deviceMountPoint = Path.GetFileName(device);
            logger.Log("Attaching volume to instance {0}, device {1}", this.instanceId, device);
            var attachVolumeResponse = this.client.AttachVolume(new AttachVolumeRequest()
            {
                InstanceId = this.instanceId,
                VolumeId = volumeId,
                Device = device,
            });

            logger.Log("Waiting for volume to reach the 'attached' state");
            await this.UntilVolumeAttachedStateAsync(volumeId, "attached");

            logger.Log("Mounting and setting up device");
            await client.MountAndSetupDeviceAsync(device, deviceMountPoint, logger);

            logger.Log("Retriving port settings");
            var portSettings = client.GetPortDescriptions(deviceMountPoint, logger).ToArray();
            logger.Log(String.Join<PortRangeDescription>(", ", portSettings));

            if (portSettings.Length > 0)
            {
                logger.Log("Authorising inbound access on these ports");
                var ipPermissions = portSettings.Select(x => new IpPermissionSpecification()
                {
                    FromPort = x.FromPort,
                    ToPort = x.ToPort,
                    IpProtocol = x.Proto,
                    IpRanges = new List<string>() { "0.0.0.0/0" },
                });
                var ingressRequest = new AuthorizeSecurityGroupIngressRequest()
                {
                    GroupName = this.securityGroupName,
                };
                ingressRequest.IpPermissions.AddRange(ipPermissions);
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

            logger.Log("Volume successfully mounted");

            return deviceMountPoint;
        }

        public async Task<string> CreateVolumeFromSnapshot(string snapshotId, IMachineInteractionProvider client, Logger logger = null)
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

        private async Task UntilStateAsync(string state)
        {
            bool gotToState = false;

            while (!gotToState)
            {
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

        public async Task DestroyAsync(Logger logger = null)
        {
            logger = logger ?? this.DefaultLogger;

            logger.Log("Starting instance termination process");

            var instanceStatus = this.GetRunningInstance();
            var groupIds = instanceStatus.GroupId;
            var keyName = instanceStatus.KeyName;
            // This excludes volumes attached to other machines as well
            var volumes = this.client.DescribeVolumes(new DescribeVolumesRequest()).DescribeVolumesResult.Volume
                .Where(x => x.Attachment.Count == 1 && x.Attachment[0].InstanceId == this.instanceId)
                .Where(x => x.Tag.Any(y => y.Key == "CreatedByEc2Manager"))
                .Select(x => x.VolumeId);

            // Detach the volumes in parallel, since it takes a nice long time
            logger.Log("Found uniquely attached volumes: {0}", string.Join(", ", volumes));
            var waitUntilDetachedTasks = new List<Task>();
            foreach (var volume in volumes)
            {
                logger.Log("Detaching volume {0}", volume);
                this.client.DetachVolume(new DetachVolumeRequest()
                {
                    Force = true,
                    InstanceId = this.instanceId,
                    VolumeId = volume,
                });

                waitUntilDetachedTasks.Add(this.UntilVolumeAttachedStateAsync(volume, "available"));
            }

            logger.Log("Waiting until volume(s) become(s) available");
            await Task.WhenAll(waitUntilDetachedTasks);

            foreach (var volume in volumes)
            {
                logger.Log("Deleting volume {0}", volume);
                this.client.DeleteVolume(new DeleteVolumeRequest()
                {
                    VolumeId = volume,
                });
            }

            logger.Log("Terminating instance");
            var termResponse = this.client.TerminateInstances(new TerminateInstancesRequest()
            {
                InstanceId = new List<string>() { this.instanceId },
            });

            logger.Log("Waiting for instance to reach the 'terminated' state");
            await this.UntilStateAsync("terminated");

            // This has to be set after the instance has been terminated
            var allInstances = this.client.DescribeInstances(new DescribeInstancesRequest()).DescribeInstancesResult.Reservation
                .Where(x => x.RunningInstance.All(y => y.InstanceState.Name != "terminated")).ToArray();

            var usedGroupIds = allInstances.SelectMany(x => x.GroupId).Distinct();
            logger.Log("Found security groups uniquely associated with instance: {0}", string.Join(", ", groupIds.Except(usedGroupIds)));

            foreach (var groupId in groupIds.Except(usedGroupIds))
            {
                logger.Log("Deleting security group {0}", groupId);
                this.client.DeleteSecurityGroup(new DeleteSecurityGroupRequest()
                {
                    GroupId = groupId,
                });
            }

            var usedKeyNames = allInstances.SelectMany(x => x.RunningInstance.Select(y => y.KeyName)).Distinct();
            if (!usedKeyNames.Contains(keyName))
            {
                logger.Log("Deleting key pair: {0}", keyName);
                this.client.DeleteKeyPair(new DeleteKeyPairRequest()
                {
                    KeyName = keyName,
                });
            }

            logger.Log("Releasing IP address {0}", this.PublicIp);
            this.client.ReleaseAddress(new ReleaseAddressRequest()
            {
                PublicIp = this.PublicIp,
            });

            logger.Log("Instance successfully terminated");
        }
    }
}
