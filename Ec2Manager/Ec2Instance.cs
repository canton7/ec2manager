using Amazon.EC2;
using Amazon.EC2.Model;
using Caliburn.Micro;
using Ec2Manager.Classes;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Ec2Manager.Ec2Manager
{
    public class Ec2Instance : PropertyChangedBase
    {
        public string InstanceId { get; private set; }
        public string Name { get; private set; }
        public InstanceSpecification Specification { get; private set; }
        public string PrivateKey { get; private set; }

        public AmazonEC2Client Client { get; private set; }
        public ILogger Logger;
        private CreationState state;

        // We can mount on xvdf -> xvdp
        private static readonly string[] mountPoints = new[]
        {
            "/dev/xvdf", "/dev/xvdg", "/dev/xvdh", "/dev/xvdi", "/dev/xvdj", "/dev/xvdk", "/dev/xvdl", "/dev/xvdm",
            "/dev/xvdn", "/dev/xvdo", "/dev/xvdp",
        };
        private AsyncSemaphore volumeMountPointLock = new AsyncSemaphore(1, 1);

        private string uniqueKey;
        private string securityGroupName
        {
            get { return "Ec2SecurityGroup-" + this.uniqueKey; }
        }
        private string keyPairName
        {
            get { return "Ec2KeyPair-" + this.uniqueKey; }
        }

        private string bidRequestId;

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
        private string instanceState;
        public string InstanceState
        {
            get { return this.instanceState; }
            set
            {
                this.instanceState = value;
                this.NotifyOfPropertyChange();
            }
        }
        private string bidStatus;
        public string BidStatus
        {
            get { return this.bidStatus; }
            set
            {
                this.bidStatus = value;
                this.NotifyOfPropertyChange();
            }
        }

        public Ec2Instance(AmazonEC2Client client, string name, InstanceSpecification specification)
        {
            this.Client = client;
            this.Name = name;
            this.Specification = specification;
            this.uniqueKey = Guid.NewGuid().ToString();

            this.state = CreationState.DoesntExist;

            this.Logger = new StubLogger();
        }

        //public Ec2Instance(AmazonEC2Client client, string instanceId)
        //{
        //    this.Client = client;
        //    this.InstanceId = instanceId;

        //    // TODO: Get name from tag (and other specification stuff)
        //    this.Name = "TODO";
        //    this.state = CreationState.CreatedNotSetup;

        //    this.Logger = new StubLogger();
        //}

        public Ec2Instance(AmazonEC2Client client, RunningInstance runningInstance)
        {
            this.Client = client;
            this.InstanceId = runningInstance.InstanceId;

            this.Reconnect(runningInstance);

            this.state = CreationState.IsSetup;

            this.Logger = new StubLogger();
        }

        public async Task SetupAsync(CancellationToken? cancellationToken = null)
        {
            if (this.state == CreationState.DoesntExist)
            {
                await this.CreateAsync(cancellationToken);
                this.state = CreationState.IsSetup;
            }
            else if (this.state == CreationState.ExistsNotSetup)
            {
                var runningInstance = await this.DescribeInstanceAsync();
                this.Reconnect(runningInstance);
                this.state = CreationState.IsSetup;
            }
        }

        #region Property Retrieval

        private async Task<RunningInstance> DescribeInstanceAsync()
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
                    describeInstancesResponse = await this.Client.RequestAsync(s => s.DescribeInstances(describeInstancesRequest));
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
                .Where(x => x.InstanceId == this.InstanceId)
                .FirstOrDefault();
        }

        #endregion

        #region Volume Management

        private Task<IEnumerable<Volume>> GetAttachedVolumesAsync()
        {
            return this.Client.RequestAsync(s => s.DescribeVolumes(new DescribeVolumesRequest()).DescribeVolumesResult.Volume
                .Where(x => x.Attachment.Any(att => att.InstanceId == this.InstanceId)));
        }

        #endregion

        #region Security Groups

        private async Task CreateSecurityGroupAsync()
        {
            this.Logger.Log("Creating a new security group: {0}", this.securityGroupName);
            var createSecurityGroupResponse = await this.Client.RequestAsync(s => s.CreateSecurityGroup(new CreateSecurityGroupRequest()
            {
                GroupName = this.securityGroupName,
                GroupDescription = "Ec2Manager-created security group",
            }));
            this.Logger.Log("Security group ID {0} created", createSecurityGroupResponse.CreateSecurityGroupResult.GroupId);
        }

        private async Task DeleteSecurityGroupAsync()
        {
            this.Logger.Log("Deleting security group {0}", this.securityGroupName);
            try
            {
                await this.Client.RequestAsync(s => s.DeleteSecurityGroup(new DeleteSecurityGroupRequest()
                {
                    GroupName = this.securityGroupName,
                }));
            }
            catch (AmazonEC2Exception e)
            {
                // It can take a long while for groups to appear, as far as this command is concerned.
                // If this happens, ignore it.
                if (e.ErrorCode == "InvalidGroup.NotFound")
                    this.Logger.Log("Failed to delete security group as Ec2 doesn't think it exists (this happens...)");
                else
                    throw;
            }
        }

        public async Task AuthorizeIngressAsync(IEnumerable<PortRangeDescription> portRanges)
        {
            if (portRanges.Count() == 0)
                return;

            this.Logger.Log("Allowing inbound access on {0}", string.Join(", ", portRanges));

            var ingressRequest = new AuthorizeSecurityGroupIngressRequest()
            {
                GroupName = this.securityGroupName,
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
                await this.Client.RequestAsync(s => s.AuthorizeSecurityGroupIngress(ingressRequest));
            }
            catch (AmazonEC2Exception e)
            {
                // Duplicate port settings are just fine
                if (e.ErrorCode != "InvalidPermission.Duplicate")
                    throw;
            }

            this.Logger.Log("Inbound access authorised");
        }

        #endregion

        #region Key Pairs

        private async Task<string> CreateKeyPairAsync()
        {
            this.Logger.Log("Creating a new key pair: {0}", this.keyPairName);
            var newKeyResponse = await this.Client.RequestAsync(s => s.CreateKeyPair(new CreateKeyPairRequest()
            {
                KeyName = this.keyPairName,
            }));
            var keyPair = newKeyResponse.CreateKeyPairResult.KeyPair;
            this.Logger.Log("Key pair created. Fingerprint {0}", keyPair.KeyFingerprint);

            return keyPair.KeyMaterial;
        }

        private async Task DeleteKeyPairAsync()
        {
            this.Logger.Log("Deleting key pair: {0}", this.keyPairName);
            await this.Client.RequestAsync(s => s.DeleteKeyPair(new DeleteKeyPairRequest()
            {
                KeyName = this.keyPairName,
            }));
        }

        #endregion

        #region IP Addresses

        private async Task<string> AllocateAddressAsync()
        {
            this.Logger.Log("Allocating an IP address");
            var allocateResponse = await this.Client.RequestAsync(s => s.AllocateAddress(new AllocateAddressRequest()));
            var publicIp = allocateResponse.AllocateAddressResult.PublicIp;
            this.Logger.Log("Ip address {0} allocated", publicIp);

            return publicIp;
        }

        private async Task AssignAddressAsync(string publicIp)
        {
            this.Logger.Log("Assigning public IP {0} to instance", this.PublicIp);
            await this.Client.RequestAsync(s => s.AssociateAddress(new AssociateAddressRequest()
            {
                InstanceId = this.InstanceId,
                PublicIp = publicIp,
            }));
            this.Logger.Log("Public IP assigned");
        }

        private async Task ReleaseIpAsync(string publicIp)
        {
            this.Logger.Log("Releasing IP address {0}", publicIp);
            await this.Client.RequestAsync(s => s.DisassociateAddress(new DisassociateAddressRequest()
            {
                PublicIp = publicIp,
            }));
            await this.Client.RequestAsync(s => s.ReleaseAddress(new ReleaseAddressRequest()
            {
                PublicIp = publicIp,
            }));
            this.Logger.Log("Ip address released");
        }

        #endregion

        #region Volumes

        public Ec2Volume CreateVolume(string source, string name)
        {
            var volume = new Ec2Volume(this, source, name);
            volume.Logger = this.Logger;
            return volume;
        }

        public Ec2Volume CreateVolume(int sizeGb, string name)
        {
            var volume = new Ec2Volume(this, name, sizeGb);
            volume.Logger = this.Logger;
            return volume;
        }

        public async Task<string> AttachVolumeAsync(Ec2Volume volume)
        {
            string mountPoint = null;

            await this.volumeMountPointLock.WithLock(async () =>
                {
                    mountPoint = mountPoints.Except((await this.GetAttachedVolumesAsync()).Select(x => x.Attachment.FirstOrDefault(y => y.InstanceId == this.InstanceId).Device)).FirstOrDefault();
                    if (mountPoint == null)
                        throw new Exception("Run out of mount points. You have too many volumes mounted!");

                    this.Logger.Log("Attaching volume to instance {0}, device {1}", this.InstanceId, mountPoint);
                    var attachVolumeResponse = await this.Client.RequestAsync(s => s.AttachVolume(new AttachVolumeRequest()
                    {
                        InstanceId = this.InstanceId,
                        VolumeId = volume.VolumeId,
                        Device = mountPoint,
                    }));
                }
            );

            return mountPoint;
        }

        public async Task DetachVolumeAsync(Ec2Volume volume)
        {
            if (volume.VolumeId == null)
                return;

            this.Logger.Log("Detaching volume {0}", volume.VolumeId);
            await this.Client.RequestAsync(s => s.DetachVolume(new DetachVolumeRequest()
            {
                Force = true,
                InstanceId = this.InstanceId,
                VolumeId = volume.VolumeId,
            }));
        }


        public async Task<IEnumerable<Ec2Volume>> ListVolumesAsync()
        {
            var volumes = new List<Ec2Volume>();
            foreach (var volume in await this.GetAttachedVolumesAsync())
            {
                var attachment = volume.Attachment.FirstOrDefault(x => x.InstanceId == this.InstanceId);
                if (attachment != null && mountPoints.Contains(attachment.Device))
                {
                    var tag = volume.Tag.FirstOrDefault(x => x.Key == "VolumeName");
                    volumes.Add(new Ec2Volume(this, volume.VolumeId, tag == null ? "Unnamed" : tag.Value, Path.GetFileName(attachment.Device)));
                }
            }

            return volumes;
        }

        #endregion

        #region Instance Creation, Setup, and Destruction

        private async Task SetupInstanceAsync(CancellationToken? cancellationToken = null)
        {
            CancellationToken token = cancellationToken.HasValue ? cancellationToken.Value : new CancellationToken();

            // Tag straight away. They might get bored and close the window while it's launching
            this.Logger.Log("Tagging instance");
            await this.Client.RequestAsync(s => s.CreateTags(new CreateTagsRequest()
            {
                ResourceId = new List<string>() { this.InstanceId },
                Tag = new List<Tag>()
                {
                    new Tag() { Key = "CreatedByEc2Manager", Value = "true" },
                    new Tag() { Key = "Name", Value = this.Name },
                    new Tag() { Key = "UniqueKey", Value = this.uniqueKey },
                },
            }));

            this.Logger.Log("Waiting for instance to reach 'running' state");
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
                this.Logger.Log("The instance is taking a long time to come up. This happens sometimes.");
                this.Logger.Log("Sometimes issuing a reboot fixes it, so trying that...");
                await this.Client.RequestAsync(s => s.RebootInstances(new RebootInstancesRequest()
                {
                    InstanceId = new List<string>() { this.InstanceId },
                }));
                await this.UntilStateAsync("running", token);
            }

            this.Logger.Log("Instance is now running");
        }

        private async Task BidForInstanceAsync(CancellationToken? cancellationToken = null)
        {
            CancellationToken token = cancellationToken.HasValue ? cancellationToken.Value : new CancellationToken();

            if (!this.Specification.SpotBidPrice.HasValue)
                throw new ArgumentNullException("specification.SpotBidPrice");

            this.Logger.Log("Bidding for new instance. Price: ${0}, AMI: {1}, size: {2}", this.Specification.SpotBidPrice.ToString(), this.Specification.Ami, this.Specification.Size.Name);
            var launchSpecification = new LaunchSpecification()
            {
                ImageId = this.Specification.Ami,
                InstanceType = this.Specification.Size.Key,
                KeyName = keyPairName,
                SecurityGroup = new List<string>() { this.securityGroupName },
            };
            if (!string.IsNullOrWhiteSpace(this.Specification.AvailabilityZone))
            {
                launchSpecification.Placement = new Placement() { AvailabilityZone = this.Specification.AvailabilityZone };
            }

            var spotResponse = await this.Client.RequestAsync(s => s.RequestSpotInstances(new RequestSpotInstancesRequest()
            {
                InstanceCount = 1,
                SpotPrice = this.Specification.SpotBidPrice.ToString(),
                LaunchSpecification = launchSpecification,
            }));
            this.bidRequestId = spotResponse.RequestSpotInstancesResult.SpotInstanceRequest[0].SpotInstanceRequestId;

            this.Logger.Log("Bid ID {0} created. Waiting for spot bid request to be fulfilled", this.bidRequestId);

            this.Logger.Log("This normally takes at least a few minutes");
            this.InstanceId = await this.UntilBidActiveAsync(this.bidRequestId, token);

            this.Logger.Log("New instance created. Instance ID: {0}", this.InstanceId);

            await this.SetupInstanceAsync(token);
        }

        private async Task CancelBidRequestAsync()
        {
            if (this.bidRequestId == null)
                return;

            this.Logger.Log("Cancelling spot bid request");

            await this.Client.RequestAsync(s => s.CancelSpotInstanceRequests(new CancelSpotInstanceRequestsRequest()
            {
                SpotInstanceRequestId = new List<string>() { this.bidRequestId },
            }));

            this.bidRequestId = null;
        }

        private async Task CreateInstanceAsync(CancellationToken? cancellationToken = null)
        {
            this.Logger.Log("Creating a new instance. AMI: {0}, size: {1}", this.Specification.Ami, this.Specification.Size.Name);
            var runInstanceRequest = new RunInstancesRequest()
            {
                ImageId = this.Specification.Ami,
                InstanceType = this.Specification.Size.Key,
                MinCount = 1,
                MaxCount = 1,
                KeyName = keyPairName,
                SecurityGroup = new List<string>() { this.securityGroupName },
            };
            if (!string.IsNullOrWhiteSpace(this.Specification.AvailabilityZone))
            {
                runInstanceRequest.Placement = new Placement() { AvailabilityZone = this.Specification.AvailabilityZone };
            }

            var runResponse = await this.Client.RequestAsync(s => s.RunInstances(runInstanceRequest));
            var instances = runResponse.RunInstancesResult.Reservation.RunningInstance;
            this.InstanceId = instances[0].InstanceId;
            this.Logger.Log("New instance created. Instance ID: {0}", this.InstanceId);

            await this.SetupInstanceAsync(cancellationToken);
        }

        private async Task TerminateAsync()
        {
            if (this.InstanceId == null)
                return;

            this.Logger.Log("Terminating instance");
            await this.Client.RequestAsync(s => s.TerminateInstances(new TerminateInstancesRequest()
            {
                InstanceId = new List<string>() { this.InstanceId },
            }));

            this.Logger.Log("Waiting for instance to reach the 'terminated' state");
            await this.UntilStateAsync("terminated");
            this.Logger.Log("Instance terminated");
        }

        #endregion

        #region "Until" methods

        private async Task<string> UntilBidActiveAsync(string spotInstanceRequestId, CancellationToken? cancellationToken = null)
        {
            string instanceId = null;
            CancellationToken token = cancellationToken.HasValue ? cancellationToken.Value : new CancellationToken();

            var bidStateRequest = new DescribeSpotInstanceRequestsRequest()
            {
                SpotInstanceRequestId = new List<string>() { spotInstanceRequestId },
            };

            while (instanceId == null)
            {
                token.ThrowIfCancellationRequested();

                var bidState = (await this.Client.RequestAsync(s => s.DescribeSpotInstanceRequests(bidStateRequest))).DescribeSpotInstanceRequestsResult.SpotInstanceRequest[0];
                this.BidStatus = bidState.Status.Code;

                if (bidState.State == "active")
                    instanceId = bidState.InstanceId;
                else if (bidState.State == "open")
                    await Task.Delay(1000, token);
                else
                    throw new Exception(string.Format("Spot bid has reached state {0} for reason {1}", bidState.State, bidState.Status));
            }

            return instanceId;
        }

        private async Task UntilStateAsync(string state, CancellationToken? cancellationToken = null)
        {
            CancellationToken token = cancellationToken.HasValue ? cancellationToken.Value : new CancellationToken();

            while (true)
            {
                token.ThrowIfCancellationRequested();

                this.InstanceState = (await this.DescribeInstanceAsync()).InstanceState.Name;

                if (this.InstanceState == state)
                    return;
                else
                    await Task.Delay(2000, token);
            }
        }

        #endregion

        #region Sequence methods

        private async Task CreateAsync(CancellationToken? cancellationToken = null)
        {
            CancellationToken token = cancellationToken.HasValue ? cancellationToken.Value : new CancellationToken();

            this.Logger.Log("Starting instance creation process");

            Exception exception = null;
            try
            {
                await this.CreateSecurityGroupAsync();
                token.ThrowIfCancellationRequested();
            }
            catch (Exception e)
            {
                exception = e;
            }
            if (exception != null)
            {
                this.Logger.Log("Error creating security group: {0}. Performing rollback", exception.Message);
                await this.DeleteSecurityGroupAsync();
                throw exception;
            }

            exception = null;
            try
            {
                await this.AuthorizeIngressAsync(new[] { new PortRangeDescription(22, 22, "tcp"), new PortRangeDescription(-1, -1, "icmp") });
                token.ThrowIfCancellationRequested();
            }
            catch (Exception e)
            {
                exception = e;
            }
            if (exception != null)
            {
                this.Logger.Log("Error authorising ingress: {0}. Performing rollback", exception.Message);
                await this.DeleteSecurityGroupAsync();
                throw exception;
            }

            exception = null;
            try
            {
                this.PrivateKey = await this.CreateKeyPairAsync();
                token.ThrowIfCancellationRequested();
            }
            catch (Exception e)
            {
                exception = e;
            }
            if (exception != null)
            {
                this.Logger.Log("Error creating key pair: {0}. Performing rollback", exception.Message);
                await this.DeleteKeyPairAsync();
                await this.DeleteSecurityGroupAsync();
                throw exception;
            }

            exception = null;
            try
            {
                if (this.Specification.IsSpotInstance)
                    await this.BidForInstanceAsync(token);
                else
                    await this.CreateInstanceAsync(token);

                token.ThrowIfCancellationRequested();
            }
            catch (Exception e)
            {
                exception = e;
            }
            if (exception != null)
            {
                this.Logger.Log("Error creating instance: {0}. Performing rollback", exception.Message);
                if (this.Specification.IsSpotInstance)
                    await this.CancelBidRequestAsync();
                await this.TerminateAsync();
                await this.DeleteKeyPairAsync();
                await this.DeleteSecurityGroupAsync();
                throw exception;
            }

            exception = null;
            try
            {
                this.PublicIp = await this.AllocateAddressAsync();
                await this.AssignAddressAsync(this.PublicIp);
                token.ThrowIfCancellationRequested();
            }
            catch (Exception e)
            {
                exception = e;
            }
            if (exception != null)
            {
                this.Logger.Log("Error allocating public IP: {0}. Performing rollback", exception.Message);
                await this.ReleaseIpAsync(this.PublicIp);
                await this.TerminateAsync();
                await this.DeleteKeyPairAsync();
                await this.DeleteSecurityGroupAsync();
                throw exception;
            }

            // We need to know this to create volumes
            if (this.Specification.AvailabilityZone == null)
                this.Specification.AvailabilityZone = (await this.DescribeInstanceAsync()).Placement.AvailabilityZone;

            this.Logger.Log("Instance has been created");
        }

        private void Reconnect(RunningInstance runningInstance)
        {
            this.Name = runningInstance.Tag.First(x => x.Key == "Name").Value;

            this.PublicIp = runningInstance.IpAddress;
            this.PublicIp = string.IsNullOrWhiteSpace(this.PublicIp) ? null : this.PublicIp;

            this.InstanceState = runningInstance.InstanceState.Name;

            var uniqueKey = runningInstance.Tag.FirstOrDefault(x => x.Key == "UniqueKey");
            if (uniqueKey == null)
                this.uniqueKey = new Guid().ToString();
            else
                this.uniqueKey = uniqueKey.Value;

            // TODO: Fetch AMI and spot bid amount. 
            var size = Ec2Connection.InstanceSizes.FirstOrDefault(x => x.Key == runningInstance.InstanceType);
            this.Specification = new InstanceSpecification(null, size, runningInstance.Placement.AvailabilityZone);
            this.bidRequestId = runningInstance.SpotInstanceRequestId;
        }

        public async Task DestroyAsync()
        {
            this.Logger.Log("Starting instance termination process");

            var instanceStatus = await this.DescribeInstanceAsync();
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
                await this.ReleaseIpAsync(this.PublicIp);
            }

            // Detach the volumes in parallel, since it takes a nice long time
            this.Logger.Log("Found uniquely attached volumes: {0}", string.Join(", ", volumes));
            await Task.WhenAll(volumes.Select(volume => new Ec2Volume(this, volume).DeleteAsync()));

            await this.TerminateAsync();

            // This has to be set after the instance has been terminated
            var allInstances = (await this.Client.RequestAsync(s => s.DescribeInstances(new DescribeInstancesRequest()))).DescribeInstancesResult.Reservation
                .Where(x => x.RunningInstance.All(y => y.InstanceState.Name != "terminated")).ToArray();

            var usedGroupIds = allInstances.SelectMany(x => x.GroupId).Distinct();
            this.Logger.Log("Found security groups uniquely associated with instance: {0}", string.Join(", ", groupIds.Except(usedGroupIds)));

            foreach (var groupId in groupIds.Except(usedGroupIds))
            {
                await this.DeleteSecurityGroupAsync();
            }

            var usedKeyNames = allInstances.SelectMany(x => x.RunningInstance.Select(y => y.KeyName)).Distinct();
            if (!usedKeyNames.Contains(keyName))
            {
                await this.DeleteKeyPairAsync();
            }

            this.Logger.Log("Instance successfully terminated");
        }

        // TODO: Use this to replace isCreated
        private enum CreationState
        {
            DoesntExist,
            ExistsNotSetup,
            IsSetup,
        }

        #endregion
    }
}
