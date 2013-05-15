﻿using System;
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
        private static readonly string[] mountPoints = new[]
        {
            "/dev/xvdf", "/dev/xvdg", "/dev/xvdh", "/dev/xvdi", "/dev/xvdj", "/dev/xvdk", "/dev/xvdl", "/dev/xvdm",
            "/dev/xvdn", "/dev/xvdo", "/dev/xvdp",
        };
        private List<string> usedMountPoints = new List<string>();
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
        private string bidStatus = null;
        public string BidStatus
        {
            get { return this.bidStatus; }
            private set
            {
                this.bidStatus = value;
                this.NotifyOfPropertyChange();
            }
        }

        private string bidRequestId = null;

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
        public Ec2Manager(string accessKey, string secretKey, ILogger logger = null)
        {
            this.client = new AmazonEC2Client(accessKey, secretKey, RegionEndpoint.EUWest1);
            this.uniqueKey = Guid.NewGuid().ToString();
            this.DefaultLogger = logger ?? new StubLogger();
        }
        /// <summary>
        /// Associate with an already-running instance
        /// </summary>
        public Ec2Manager(string accessKey, string secretKey, string instanceId, ILogger logger = null)
        {
            this.client = new AmazonEC2Client(accessKey, secretKey, RegionEndpoint.EUWest1);
            this.DefaultLogger = logger ?? new StubLogger();
            this.InstanceId = instanceId;
        }

        public async Task<double> GetCurrentSpotPriceAsync(string instanceSize)
        {
            var result = await this.RequestAsync(s => s.DescribeSpotPriceHistory(new DescribeSpotPriceHistoryRequest()
            {
                InstanceType = new List<string>() { instanceSize },
                ProductDescription = new List<string>() { "Linux/UNIX" },
                MaxResults = 1,
            }));

            return double.Parse(result.DescribeSpotPriceHistoryResult.SpotPriceHistory[0].SpotPrice);
        }

        private Task RequestAsync(Action<AmazonEC2Client> method)
        {
            return this.RequestAsync<object>(s => { method(s); return null; });
        }

        private Task<T> RequestAsync<T>(Func<AmazonEC2Client, T> method)
        {
            return Task.Run<T>(() => method(this.client));
        }

        private async Task<RunningInstance> GetRunningInstanceAsync()
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
                    describeInstancesResponse = await this.RequestAsync(s => s.DescribeInstances(describeInstancesRequest));
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
                if (this.usedMountPoints.Count == mountPoints.Length)
                    throw new Exception("Run out of mount points. You have too many volumes mounted!");

                result = mountPoints.Except(this.usedMountPoints).First();
                this.usedMountPoints.Add(result);
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

                this.InstanceState = (await this.GetRunningInstanceAsync()).InstanceState.Name;

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

        private async Task<string> UntilBidActiveAsync(string spotInstanceRequestId, CancellationToken? cancellationToken = null)
        {
            string instanceId = null;

            var bidStateRequest = new DescribeSpotInstanceRequestsRequest()
            {
                SpotInstanceRequestId = new List<string>() { spotInstanceRequestId },
            };

            while (instanceId == null)
            {
                if (cancellationToken.HasValue)
                    cancellationToken.Value.ThrowIfCancellationRequested();

                var bidState = (await this.RequestAsync(s => s.DescribeSpotInstanceRequests(bidStateRequest))).DescribeSpotInstanceRequestsResult.SpotInstanceRequest[0];
                this.BidStatus = bidState.Status.Code;

                if (bidState.State == "active")
                {
                    instanceId = bidState.InstanceId;
                }
                else if (bidState.State == "open")
                {
                    await Task.Delay(1000);
                }
                else
                {
                    throw new Exception(string.Format("Spot bid has reached state {0} for reason {1}", bidState.State, bidState.Status));
                }
            }

            return instanceId;
        }

        private async Task UntilVolumeStateAsync(string volumeId, string state, CancellationToken? cancellationToken = null)
        {
            bool gotToState = false;
            CancellationToken token = cancellationToken.HasValue ? cancellationToken.Value : new CancellationToken();

            var describeVolumesRequest = new DescribeVolumesRequest()
            {
                VolumeId = new List<string>() { volumeId },
            };

            while (!gotToState)
            {
                token.ThrowIfCancellationRequested();

                var describeVolumesResponse = await this.RequestAsync(s => s.DescribeVolumes(describeVolumesRequest));
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

        private async Task UntilVolumeAttachedStateAsync(string volumeId, string state, bool allowNone = true, CancellationToken? cancellationToken = null)
        {
            bool gotToState = false;
            CancellationToken token = cancellationToken.HasValue ? cancellationToken.Value : new CancellationToken();

            var describeVolumesRequest = new DescribeVolumesRequest()
            {
                VolumeId = new List<string>() { volumeId },
            };

            while (!gotToState)
            {
                token.ThrowIfCancellationRequested();

                var describeVolumesResponse = await this.RequestAsync(s => s.DescribeVolumes(describeVolumesRequest));
                var volume = describeVolumesResponse.DescribeVolumesResult.Volume
                    .FirstOrDefault(x => x.VolumeId == volumeId);

                if (volume != null && ((volume.Attachment.Count == 0 && allowNone) || volume.Attachment.FirstOrDefault(x => x.InstanceId == this.InstanceId).Status == state))
                {
                    gotToState = true;
                }
                else
                {
                    await Task.Delay(1000);
                }
            }
        }

        private async Task UntilSnapshotStateAsync(string snapshotId, string state, CancellationToken? cancellationToken = null)
        {
            bool gotToState = false;
            CancellationToken token = cancellationToken.HasValue ? cancellationToken.Value : new CancellationToken();

            var describeSnapshotsRequest = new DescribeSnapshotsRequest()
            {
                SnapshotId = new List<string>() { snapshotId },
            };

            while (!gotToState)
            {
                token.ThrowIfCancellationRequested();

                var describeSnapshotsResponse = await this.RequestAsync(s => s.DescribeSnapshots(describeSnapshotsRequest));
                var status = describeSnapshotsResponse.DescribeSnapshotsResult.Snapshot.FirstOrDefault(x => x.SnapshotId == snapshotId).Status;

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

        private async Task CreateSecurityGroupAsync(string groupName, ILogger logger = null)
        {
            logger = logger ?? this.DefaultLogger;

            logger.Log("Creating a new security group: {0}", this.securityGroupName);
            var createSecurityGroupResponse = await this.RequestAsync(s => s.CreateSecurityGroup(new CreateSecurityGroupRequest()
            {
                GroupName = groupName,
                GroupDescription = "Ec2Manager-created security group",
            }));
            logger.Log("Security group ID {0} created", createSecurityGroupResponse.CreateSecurityGroupResult.GroupId);
        }

        private async Task AuthorizeIngressAsync(string groupName, IEnumerable<PortRangeDescription> portRanges, ILogger logger = null)
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
                await this.RequestAsync(s => s.AuthorizeSecurityGroupIngress(ingressRequest));
            }
            catch (AmazonEC2Exception e)
            {
                // Duplicate port settings are just fine
                if (e.ErrorCode != "InvalidPermission.Duplicate")
                    throw;
            }

            logger.Log("Inbound access authorised");
        }

        private async Task<string> CreateKeyPairAsync(string keyPairName, ILogger logger = null)
        {
            logger = logger ?? this.DefaultLogger;

            logger.Log("Creating a new key pair: {0}", this.keyPairName);
            var newKeyResponse = await this.RequestAsync(s => s.CreateKeyPair(new CreateKeyPairRequest()
            {
                KeyName = keyPairName,
            }));
            var keyPair = newKeyResponse.CreateKeyPairResult.KeyPair;
            logger.Log("Key pair created. Fingerprint {0}", keyPair.KeyFingerprint);

            return keyPair.KeyMaterial;
        }

        private async Task CreateInstanceAsync(string ami, string size, string keyPairName, string securityGroup, string name, string availabilityZone = null, CancellationToken? cancellationToken = null, ILogger logger = null)
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

            var runResponse = await this.RequestAsync(s => s.RunInstances(runInstanceRequest));
            var instances = runResponse.RunInstancesResult.Reservation.RunningInstance;
            this.InstanceId = instances[0].InstanceId;
            logger.Log("New instance created. Instance ID: {0}", this.InstanceId);

            await this.SetupInstanceAsync(name, cancellationToken);
        }

        private async Task BidForInstanceAsync(string ami, string size, string keyPairName, string securityGroup, string name, double spotBidPrice, string availabilityZone = null, CancellationToken? cancellationToken = null, ILogger logger = null)
        {
            logger = logger ?? this.DefaultLogger;
            CancellationToken token = cancellationToken.HasValue ? cancellationToken.Value : new CancellationToken();

            logger.Log("Bidding for new instance. Price: ${0}, AMI: {1}, size: {2}", spotBidPrice.ToString(), ami, size);
            var launchSpecification = new LaunchSpecification()
            {
                ImageId = ami,
                InstanceType = size,
                KeyName = keyPairName,
                SecurityGroup = new List<string>() { securityGroup },
            };
            if (!string.IsNullOrWhiteSpace(availabilityZone))
            {
                launchSpecification.Placement = new Placement() { AvailabilityZone = availabilityZone };
            }

            var spotResponse = await this.RequestAsync(s => s.RequestSpotInstances(new RequestSpotInstancesRequest()
            {
                InstanceCount = 1,
                SpotPrice = spotBidPrice.ToString(),
                LaunchSpecification = launchSpecification,
            }));
            this.bidRequestId = spotResponse.RequestSpotInstancesResult.SpotInstanceRequest[0].SpotInstanceRequestId;

            logger.Log("Bid ID {0} created. Waiting for spot bid request to be fulfilled", this.bidRequestId);
            logger.Log("This normally takes at least a few minutes");
            this.InstanceId = await this.UntilBidActiveAsync(this.bidRequestId, token);
            logger.Log("New instance created. Instance ID: {0}", this.InstanceId);

            await this.SetupInstanceAsync(name, token);
        }

        private async Task CancelBidRequestAsync(ILogger logger = null)
        {
            logger = logger ?? this.DefaultLogger;

            if (this.bidRequestId == null)
                return;

            logger.Log("Cancelling spot bid request");

            await this.RequestAsync(s => s.CancelSpotInstanceRequests(new CancelSpotInstanceRequestsRequest()
            {
                SpotInstanceRequestId = new List<string>() { this.bidRequestId },
            }));

            this.bidRequestId = null;
        }

        private async Task SetupInstanceAsync(string name, CancellationToken? cancellationToken = null, ILogger logger = null)
        {
            logger = logger ?? this.DefaultLogger;
            CancellationToken token = cancellationToken.HasValue ? cancellationToken.Value : new CancellationToken();

            // Tag straight away. They might get bored and close the window while it's launching
            logger.Log("Tagging instance");
            await this.RequestAsync(s => s.CreateTags(new CreateTagsRequest()
            {
                ResourceId = new List<string>() { this.InstanceId },
                Tag = new List<Tag>()
                {
                    new Tag() { Key = "CreatedByEc2Manager", Value = "true" },
                    new Tag() { Key = "Name", Value = name },
                    new Tag() { Key = "UniqueKey", Value = this.uniqueKey },
                },
            }));

            logger.Log("Waiting for instance to reach 'running' state");
            // Sometimes (I have no idea why) AWS reports the instance as pending when the console shows it running
            // The observed fix is to restart it
            var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(45));
            var cts = CancellationTokenSource.CreateLinkedTokenSource(token, timeoutCts.Token);
            bool needsRestart = false;
            try
            {
                await this.UntilStateAsync("running", cts.Token);
            }
            catch (TaskCanceledException)
            {
                if (timeoutCts.IsCancellationRequested && !token.IsCancellationRequested)
                    needsRestart = true;
                else
                    throw;
            }
            if (needsRestart)
            {
                logger.Log("The instance is taking a long time to come up. This happens sometimes.");
                logger.Log("Sometimes issuing a reboot fixes it, so trying that...");
                await this.RequestAsync(s => s.RebootInstances(new RebootInstancesRequest()
                {
                    InstanceId = new List<string>() { this.InstanceId },
                }));
                await this.UntilStateAsync("running", token);
            }

            logger.Log("Instance is now running");
        }

        private async Task<string> AllocateAddressAsync(ILogger logger = null)
        {
            logger = logger ?? this.DefaultLogger;

            logger.Log("Allocating an IP address");
            var allocateResponse = await this.RequestAsync(s => s.AllocateAddress(new AllocateAddressRequest()));
            var publicIp = allocateResponse.AllocateAddressResult.PublicIp;
            logger.Log("Ip address {0} allocated", publicIp);

            return publicIp;
        }

        private async Task AssignAddressAsync(string publicIp, ILogger logger = null)
        {
            logger = logger ?? this.DefaultLogger;

            logger.Log("Assigning public IP {0} to instance", this.PublicIp);
            await this.RequestAsync(s => s.AssociateAddress(new AssociateAddressRequest()
            {
                InstanceId = this.InstanceId,
                PublicIp = publicIp,
            }));
            logger.Log("Public IP assigned");
        }

        private async Task ReleaseIpAsync(string publicIp, ILogger logger = null)
        {
            logger = logger ?? this.DefaultLogger;

            logger.Log("Releasing IP address {0}", publicIp);
            await this.RequestAsync(s => s.DisassociateAddress(new DisassociateAddressRequest()
            {
                PublicIp = publicIp,
            }));
            await this.RequestAsync(s => s.ReleaseAddress(new ReleaseAddressRequest()
            {
                PublicIp = publicIp,
            }));
            logger.Log("Ip address released");
        }

        private Task<IEnumerable<Volume>> GetAttachedVolumesAsync()
        {
            return this.RequestAsync(s => s.DescribeVolumes(new DescribeVolumesRequest()).DescribeVolumesResult.Volume
                .Where(x => x.Attachment.Any(att => att.InstanceId == this.InstanceId)));
        }

        public async Task<IEnumerable<VolumeDescription>> GetAttachedVolumeDescriptionsAsync()
        {
            var items = new List<VolumeDescription>();
            foreach (var volume in await this.GetAttachedVolumesAsync())
            {
                var attachment = volume.Attachment.FirstOrDefault(x => x.InstanceId == this.InstanceId);
                if (attachment != null && mountPoints.Contains(attachment.Device))
                {
                    var tag = volume.Tag.FirstOrDefault(x => x.Key == "VolumeName");
                    items.Add(new VolumeDescription(Path.GetFileName(attachment.Device), volume.VolumeId, tag == null ? "Unnamed" : tag.Value));
                }
            }

            return items;
        }

        public async Task DeleteVolumeAsync(string volumeId, ILogger logger = null)
        {
            logger = logger ?? this.DefaultLogger;

            logger.Log("Detaching volume {0}", volumeId);
            await this.RequestAsync(s => s.DetachVolume(new DetachVolumeRequest()
            {
                Force = true,
                InstanceId = this.InstanceId,
                VolumeId = volumeId,
            }));

            logger.Log("Waiting until volume {0} becomes available", volumeId);
            await this.UntilVolumeAttachedStateAsync(volumeId, "available");

            logger.Log("Deleting volume {0}", volumeId);
            await this.RequestAsync(s => s.DeleteVolume(new DeleteVolumeRequest()
            {
                VolumeId = volumeId,
            }));

            await this.ResetAttachedVolumesAsync(logger);

            logger.Log("Volume {0} deleted", volumeId);
        }

        private async Task TerminateInstanceAsync(ILogger logger = null)
        {
            if (this.InstanceId == null)
                return;

            logger = logger ?? this.DefaultLogger;

            logger.Log("Terminating instance");
            await this.RequestAsync(s => s.TerminateInstances(new TerminateInstancesRequest()
            {
                InstanceId = new List<string>() { this.InstanceId },
            }));

            logger.Log("Waiting for instance to reach the 'terminated' state");
            await this.UntilStateAsync("terminated");
            logger.Log("Instance terminated");
        }

        private async Task DeleteSecurityGroupAsync(string groupId, ILogger logger = null)
        {
            logger = logger ?? this.DefaultLogger;

            logger.Log("Deleting security group {0}", groupId);
            try
            {
                await this.RequestAsync(s => s.DeleteSecurityGroup(new DeleteSecurityGroupRequest()
                {
                    GroupId = groupId,
                }));
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

        private async Task DeleteKeyPairAsync(string keyName, ILogger logger = null)
        {
            logger = logger ?? this.DefaultLogger;

            logger.Log("Deleting key pair: {0}", keyName);
            await this.RequestAsync(s => s.DeleteKeyPair(new DeleteKeyPairRequest()
            {
                KeyName = keyName,
            }));
        }

        private async Task AttachVolumeAsync(string volumeId, string device, CancellationToken? cancellationToken = null, ILogger logger = null)
        {
            logger = logger ?? this.DefaultLogger;
            CancellationToken token = cancellationToken.HasValue ? cancellationToken.Value : new CancellationToken();

            logger.Log("Attaching volume to instance {0}, device {1}", this.InstanceId, device);
            var attachVolumeResponse = await this.RequestAsync(s => s.AttachVolume(new AttachVolumeRequest()
            {
                InstanceId = this.InstanceId,
                VolumeId = volumeId,
                Device = device,
            }));

            logger.Log("Waiting for volume to reach the 'attached' state");
            await this.UntilVolumeAttachedStateAsync(volumeId, "attached", cancellationToken: token);
        }

        public async Task CreateAsync(string instanceAmi, string instanceSize, string availabilityZone = null, double? spotBidPrice = null, CancellationToken? cancellationToken = null, ILogger logger = null)
        {
            logger = logger ?? this.DefaultLogger;
            CancellationToken token = cancellationToken.HasValue ? cancellationToken.Value : new CancellationToken();

            logger.Log("Starting instance creation process");

            Exception exception = null;
            try
            {
                await this.CreateSecurityGroupAsync(this.securityGroupName, logger);
                token.ThrowIfCancellationRequested();
            }
            catch (Exception e)
            {
                exception = e;
            }
            if (exception != null)
            {
                logger.Log("Error creating security group: {0}. Performing rollback", exception.Message);
                await this.DeleteSecurityGroupAsync(this.securityGroupName, logger);
                throw exception;
            }

            exception = null;
            try
            {
                await this.AuthorizeIngressAsync(this.securityGroupName, new[] { new PortRangeDescription(22, 22, "tcp"), new PortRangeDescription(-1, -1, "icmp") }, logger);
                token.ThrowIfCancellationRequested();
            }
            catch (Exception e)
            {
                exception = e;
            }
            if (exception != null)
            {
                logger.Log("Error authorising ingress: {0}. Performing rollback", exception.Message);
                await this.DeleteSecurityGroupAsync(this.securityGroupName, logger);
                throw exception;
            }

            exception = null;
            try
            {
                this.PrivateKey = await this.CreateKeyPairAsync(this.keyPairName, logger);
                token.ThrowIfCancellationRequested();
            }
            catch (Exception e)
            {
                exception = e;
            }
            if (exception != null)
            {
                logger.Log("Error creating key pair: {0}. Performing rollback", exception.Message);
                await this.DeleteKeyPairAsync(this.keyPairName, logger);
                await this.DeleteSecurityGroupAsync(this.securityGroupName, logger);
                throw exception;
            }

            exception = null;
            try
            {
                if (spotBidPrice.HasValue)
                {
                    await this.BidForInstanceAsync(instanceAmi, instanceSize, this.keyPairName, this.securityGroupName, this.Name, spotBidPrice.Value, availabilityZone, token, logger);
                }
                else
                {
                    await this.CreateInstanceAsync(instanceAmi, instanceSize, this.keyPairName, this.securityGroupName, this.Name, availabilityZone, token, logger);
                }
                token.ThrowIfCancellationRequested();
            }
            catch (Exception e)
            {
                exception = e;
            }
            if (exception != null)
            {
                logger.Log("Error creating instance: {0}. Performing rollback", exception.Message);
                if (spotBidPrice.HasValue)
                    await this.CancelBidRequestAsync();
                await this.TerminateInstanceAsync(logger);
                await this.DeleteKeyPairAsync(this.keyPairName, logger);
                await this.DeleteSecurityGroupAsync(this.securityGroupName, logger);
                throw exception;
            }

            exception = null;
            try
            {
                this.PublicIp = await this.AllocateAddressAsync(logger);
                await this.AssignAddressAsync(this.PublicIp, logger);
                token.ThrowIfCancellationRequested();
            }
            catch (Exception e)
            {
                exception = e;
            }
            if (exception != null)
            {
                logger.Log("Error allocating public IP: {0}. Performing rollback", exception.Message);
                await this.ReleaseIpAsync(this.PublicIp, logger);
                await this.TerminateInstanceAsync(logger);
                await this.DeleteKeyPairAsync(this.keyPairName, logger);
                await this.DeleteSecurityGroupAsync(this.securityGroupName, logger);
                throw exception;
            }

            logger.Log("Instance has been created");
        }

        private async Task ResetAttachedVolumesAsync(ILogger logger = null)
        {
            logger = logger ?? this.DefaultLogger;

            logger.Log("Resetting list of attached devices");
            var volumes = await this.GetAttachedVolumesAsync();
            lock (this.volumeMountPointLock)
            {
                this.usedMountPoints.Clear();
                foreach (var volume in volumes)
                {
                    var attachment = volume.Attachment.FirstOrDefault(x => x.InstanceId == this.InstanceId);
                    if (attachment != null && mountPoints.Contains(attachment.Device))
                        this.usedMountPoints.Add(attachment.Device);
                }
            }
        }

        public async Task ReconnectAsync(ILogger logger = null)
        {
            logger = logger ?? this.DefaultLogger;

            var runningInstance = await this.GetRunningInstanceAsync();
            
            this.PublicIp = runningInstance.IpAddress;
            this.PublicIp = string.IsNullOrWhiteSpace(this.PublicIp) ? null : this.PublicIp;

            this.InstanceState = runningInstance.InstanceState.Name;

            var uniqueKey = runningInstance.Tag.FirstOrDefault(x => x.Key == "UniqueKey");
            if (uniqueKey == null)
                this.uniqueKey = new Guid().ToString();
            else
                this.uniqueKey = uniqueKey.Value;

            await this.ResetAttachedVolumesAsync(logger);
        }

        public async Task<string> MountVolumeAsync(string volumeId, IMachineInteractionProvider client, string name = null, CancellationToken? cancellationToken = null, ILogger logger = null)
        {
            logger = logger ?? this.DefaultLogger;
            CancellationToken token = cancellationToken.HasValue ? cancellationToken.Value : new CancellationToken();
            bool weCreatedVolume = false;

            if (volumeId.StartsWith("snap-"))
            {
                weCreatedVolume = true;
                try
                {
                    volumeId = await this.CreateVolumeFromSnapshotAsync(volumeId, client, name, token, logger);
                    token.ThrowIfCancellationRequested();
                }
                catch (Exception e)
                {
                    logger.Log("Error creating volume from snapshot: {0}", e.Message);
                    throw;
                }
            }
            else if (!volumeId.StartsWith("vol-"))
                throw new Exception("Volume ID must start with vol- or snap-");

            string device = null;
            string deviceMountPoint = null;
            Exception exception = null;
            try
            {
                device = this.GetNextDevice();
                deviceMountPoint = Path.GetFileName(device);
                await this.AttachVolumeAsync(volumeId, device, token, logger);
                token.ThrowIfCancellationRequested();

                logger.Log("Mounting and setting up device");
                await client.MountAndSetupDeviceAsync(device, deviceMountPoint, logger);
                token.ThrowIfCancellationRequested();

                logger.Log("Retriving port settings");
                var portSettings = client.GetPortDescriptions(deviceMountPoint, logger).ToArray();
                token.ThrowIfCancellationRequested();

                await this.AuthorizeIngressAsync(this.securityGroupName, portSettings, logger);
                token.ThrowIfCancellationRequested();
            }
            catch (Exception e)
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

        public async Task<string> CreateVolumeFromSnapshotAsync(string snapshotId, IMachineInteractionProvider client, string volumeName = null, CancellationToken? cancellationToken = null, ILogger logger = null)
        {
            logger = logger ?? this.DefaultLogger;
            CancellationToken token = cancellationToken.HasValue ? cancellationToken.Value : new CancellationToken();

            logger.Log("Starting device mount process. Snapshot ID: {0}", snapshotId);

            logger.Log("Creating EBS volume based on snapshot");
            var availabilityZone = (await this.GetRunningInstanceAsync()).Placement.AvailabilityZone;
            var createVolumeResponse = await this.RequestAsync(s => s.CreateVolume(new CreateVolumeRequest()
            {
                SnapshotId = snapshotId,
                VolumeType = "standard",
                AvailabilityZone = availabilityZone,
            }));
            var volumeId = createVolumeResponse.CreateVolumeResult.Volume.VolumeId;
            logger.Log("Volume ID {0} created", volumeId);

            logger.Log("Tagging volume, so we know we can remove it later");
            var name = volumeName == null ? "Unnamed" : this.Name + " - " + volumeName;
            await this.RequestAsync(s => s.CreateTags(new CreateTagsRequest()
            {
                ResourceId = new List<string>() { volumeId },
                Tag = new List<Tag>()
                {
                    new Tag() { Key = "CreatedByEc2Manager", Value = "true" },
                    new Tag() { Key = "Name", Value = name },
                    new Tag() { Key = "VolumeName", Value = volumeName },
                },
            }));

            logger.Log("Waiting for volume to reach the 'available' state");
            await this.UntilVolumeStateAsync(volumeId, "available", token);

            return volumeId;
        }

        private async Task<string> CreateSnapshotFromVolumeAsync(string volumeId, string snapshotName, string snapshotDescription, bool isPublic, CancellationToken? cancellationToken = null, ILogger logger = null)
        {
            logger = logger ?? this.DefaultLogger;
            CancellationToken token = cancellationToken.HasValue ? cancellationToken.Value : new CancellationToken();

            logger.Log("Starting to create snapshot");

            var response = await this.RequestAsync(s => s.CreateSnapshot(new CreateSnapshotRequest()
            {
                VolumeId = volumeId,
                Description = snapshotDescription,
            }));
            var snapshotId = response.CreateSnapshotResult.Snapshot.SnapshotId;

            logger.Log("Waiting for snapshot to reach the 'completed' state");
            await this.UntilSnapshotStateAsync(snapshotId, "completed", cancellationToken);

            logger.Log("Tagging snapshot"); 
            await this.RequestAsync(s => s.CreateTags(new CreateTagsRequest()
            {
                ResourceId = new List<string>() { snapshotId },
                Tag = new List<Tag>()
                {
                    new Tag() { Key = "Name", Value = snapshotName },
                },
            }));

            if (isPublic)
            {
                logger.Log("Setting perissions to public");
                await this.RequestAsync(s => s.ModifySnapshotAttribute(new ModifySnapshotAttributeRequest()
                {
                    SnapshotId = snapshotId,
                    Attribute = "CreateVolumePermission",
                    UserGroup = new List<string>() { "all" },
                }));
            };

            return snapshotId;
        }

        public async Task<IEnumerable<Tuple<string, string>>> ListInstancesAsync()
        {
            var instances = (await this.RequestAsync(s => s.DescribeInstances(new DescribeInstancesRequest()))).DescribeInstancesResult.Reservation
                .SelectMany(reservation => reservation.RunningInstance.Where(instance => instance.InstanceState.Name == "running" && instance.Tag.Any(tag => tag.Key == "CreatedByEc2Manager")));

           return instances.Select(x => new Tuple<string, string>(x.InstanceId, (x.Tag.FirstOrDefault(tag => tag.Key == "Name") ?? new Tag() { Value = "No Name" }).Value));
        }

        public async Task DestroyAsync(ILogger logger = null)
        {
            logger = logger ?? this.DefaultLogger;

            logger.Log("Starting instance termination process");

            var instanceStatus = await this.GetRunningInstanceAsync();
            var groupIds = instanceStatus.GroupId;
            var keyName = instanceStatus.KeyName;
            // This excludes volumes attached to other machines as well
            var volumes = (await this.GetAttachedVolumesAsync())
                .Where(x => x.Attachment.Count == 1)
                .Where(x => x.Tag.Any(y => y.Key == "CreatedByEc2Manager"))
                .Select(x => x.VolumeId);


            // Public IPs are a limited resource and people are impatient. Make sure we release
            // the IP before they get too bored
            if (this.PublicIp != null)
            {
                await this.ReleaseIpAsync(this.PublicIp, logger);
            }

            // Detach the volumes in parallel, since it takes a nice long time
            logger.Log("Found uniquely attached volumes: {0}", string.Join(", ", volumes));
            await Task.WhenAll(volumes.Select(volume => this.DeleteVolumeAsync(volume, logger)));

            await this.TerminateInstanceAsync(logger);

            // This has to be set after the instance has been terminated
            var allInstances = (await this.RequestAsync(s => s.DescribeInstances(new DescribeInstancesRequest()))).DescribeInstancesResult.Reservation
                .Where(x => x.RunningInstance.All(y => y.InstanceState.Name != "terminated")).ToArray();

            var usedGroupIds = allInstances.SelectMany(x => x.GroupId).Distinct();
            logger.Log("Found security groups uniquely associated with instance: {0}", string.Join(", ", groupIds.Except(usedGroupIds)));

            foreach (var groupId in groupIds.Except(usedGroupIds))
            {
                await this.DeleteSecurityGroupAsync(groupId, logger);
            }

            var usedKeyNames = allInstances.SelectMany(x => x.RunningInstance.Select(y => y.KeyName)).Distinct();
            if (!usedKeyNames.Contains(keyName))
            {
                await this.DeleteKeyPairAsync(keyName, logger);
            }

            logger.Log("Instance successfully terminated");
        }

        public async Task<string> CreateSnapshotAsync(string volumeId, string snapshotName, string snapshotDescription, bool isPublic, CancellationToken? cancellationToken = null, ILogger logger = null)
        {
            logger = logger ?? this.DefaultLogger;
            CancellationToken token = cancellationToken.HasValue ? cancellationToken.Value : new CancellationToken();

            // What to do if there's an error?
            var snapshotId = await this.CreateSnapshotFromVolumeAsync(volumeId, snapshotName, snapshotDescription, isPublic, cancellationToken, logger);

            return snapshotId;
        }

        public class VolumeDescription
        {
            public string MountPointDir { get; private set; }
            public string VolumeId { get; private set; }
            public string VolumeName { get; private set; }

            public VolumeDescription(string mountPointDir, string volumeId, string volumeName)
            {
                this.MountPointDir = mountPointDir;
                this.VolumeId = volumeId;
                this.VolumeName = volumeName;
            }
        }

        private class StubLogger : ILogger
        {
            public void Log(string message)
            {
            }

            public void Log(string format, params string[] parameters)
            {
            }

            public void LogFromStream(Stream stream, IAsyncResult asynch)
            {
            }
        }
    }
}
