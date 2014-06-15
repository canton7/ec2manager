using Amazon.EC2;
using Amazon.EC2.Model;
using Stylet;
using Ec2Manager.Classes;
using Ec2Manager.Configuration;
using Ec2Manager.Utilities;
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

        private KeyPair privateKeyPair;
        public string PrivateKey
        {
            get { return this.privateKeyPair == null ? null : this.privateKeyPair.KeyMaterial; }
        }

        private Config config;

        public AmazonEC2Client Client { get; private set; }
        public ILogger Logger;
        private CreationState state;

        // We can mount on xvdf -> xvdp
        private static readonly string[] mountPoints = new[]
        {
            "/dev/xvdf", "/dev/xvdg", "/dev/xvdh", "/dev/xvdi", "/dev/xvdj", "/dev/xvdk", "/dev/xvdl", "/dev/xvdm",
            "/dev/xvdn", "/dev/xvdo", "/dev/xvdp",
        };
        private SemaphoreSlim volumeMountPointLock = new SemaphoreSlim(1, 1);

        private string uniqueKey;
        public string SecurityGroupName
        {
            get { return "Ec2SecurityGroup-" + this.uniqueKey; }
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

        public Ec2Instance(AmazonEC2Client client, Config config, string name, InstanceSpecification specification)
        {
            this.Client = client;
            this.config = config;
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

        public Ec2Instance(AmazonEC2Client client, Config config, Instance runningInstance)
        {
            this.Client = client;
            this.config = config;
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

        private async Task<Instance> DescribeInstanceAsync()
        {
            bool worked = false;
            DescribeInstancesResponse describeInstancesResponse = null;

            var describeInstancesRequest = new DescribeInstancesRequest()
            {
                InstanceIds = new List<string>() { this.InstanceId },
            };

            while (!worked)
            {
                try
                {
                    describeInstancesResponse = await this.Client.DescribeInstancesAsync(describeInstancesRequest);
                    worked = true;
                }
                catch (AmazonEC2Exception e)
                {
                    if (e.ErrorCode != "InvalidInstanceID.NotFound")
                        throw;
                }
            }

            return describeInstancesResponse.Reservations
                .SelectMany(x => x.Instances)
                .Where(x => x.InstanceId == this.InstanceId)
                .FirstOrDefault();
        }

        #endregion

        #region Volume Management

        private async Task<IEnumerable<Volume>> GetAttachedVolumesAsync()
        {
            return (await this.Client.DescribeVolumesAsync(new DescribeVolumesRequest())).Volumes
                .Where(x => x.Attachments.Any(att => att.InstanceId == this.InstanceId));
        }

        #endregion

        #region Security Groups

        private async Task CreateSecurityGroupAsync()
        {
            this.Logger.Log("Creating a new security group: {0}", this.SecurityGroupName);
            var createSecurityGroupResponse = await this.Client.CreateSecurityGroupAsync(new CreateSecurityGroupRequest()
            {
                GroupName = this.SecurityGroupName,
                Description = "Ec2Manager-created security group",
            });
            this.Logger.Log("Security group ID {0} created", createSecurityGroupResponse.GroupId);
        }

        private async Task DeleteSecurityGroupAsync()
        {
            this.Logger.Log("Deleting security group {0}", this.SecurityGroupName);
            try
            {
                await this.Client.DeleteSecurityGroupAsync(new DeleteSecurityGroupRequest()
                {
                    GroupName = this.SecurityGroupName,
                });
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
                GroupName = this.SecurityGroupName,
                IpPermissions = portRanges.Select(x => new IpPermission()
                {
                    IpProtocol = x.Proto,
                    FromPort = x.FromPort,
                    ToPort = x.ToPort,
                    IpRanges = new List<string>() { "0.0.0.0/0" },
                }).ToList(),
            };

            try
            {
                await this.Client.AuthorizeSecurityGroupIngressAsync(ingressRequest);
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

        private async Task<KeyPair> EnsureKeyPairCreatedAsync()
        {
            KeyPair keyPair = null;
            var existingKey = this.config.LoadKey();

            // If we've got a key, check that it still exists on amazon
            if (existingKey != null)
            {
                this.Logger.Log("Saved key pair found. Fingerprint: {0}", existingKey.Value.Fingerprint);

                var response = await this.Client.DescribeKeyPairsAsync(new DescribeKeyPairsRequest()
                {
                    Filters = new List<Filter>()
                    {
                        new Filter() { Name = "fingerprint", Values = new List<string>() { existingKey.Value.Fingerprint } },
                    },
                });

                var keyPairInfo = response.KeyPairs.FirstOrDefault();
                if (keyPairInfo != null)
                {
                    keyPair = new KeyPair()
                    {
                        KeyFingerprint = keyPairInfo.KeyFingerprint,
                        KeyName = keyPairInfo.KeyName,
                        KeyMaterial = existingKey.Value.Key,
                    };
                }
            }

            // If we don't have a keypair locally, or we do but it isn't on amazon
            if (keyPair == null)
            {
                this.Logger.Log("Creating key pair");

                var response = await this.Client.CreateKeyPairAsync(new CreateKeyPairRequest()
                {
                    KeyName = String.Format("Ec2Manager-{0}-{1}", Environment.MachineName, Guid.NewGuid().ToString()),
                });

                keyPair = response.KeyPair;

                this.Logger.Log("Key pair created. Fingerprint: {0}", keyPair.KeyFingerprint);
                    
                this.config.SaveKey(new KeyDescription(keyPair.KeyMaterial, keyPair.KeyFingerprint));
            }

            return keyPair;
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

            await this.volumeMountPointLock.WaitAsync();
            {
                mountPoint = mountPoints.Except((await this.GetAttachedVolumesAsync()).Select(x => x.Attachments.FirstOrDefault(y => y.InstanceId == this.InstanceId).Device)).FirstOrDefault();
                if (mountPoint == null)
                    throw new Exception("Run out of mount points. You have too many volumes mounted!");

                this.Logger.Log("Attaching volume to instance {0}, device {1}", this.InstanceId, mountPoint);
                var attachVolumeResponse = await this.Client.AttachVolumeAsync(new AttachVolumeRequest()
                {
                    InstanceId = this.InstanceId,
                    VolumeId = volume.VolumeId,
                    Device = mountPoint,
                });
            }
            this.volumeMountPointLock.Release();

            return mountPoint;
        }

        public async Task DetachVolumeAsync(Ec2Volume volume)
        {
            if (volume.VolumeId == null)
                return;

            this.Logger.Log("Detaching volume {0}", volume.VolumeId);
            await this.Client.DetachVolumeAsync(new DetachVolumeRequest()
            {
                Force = true,
                InstanceId = this.InstanceId,
                VolumeId = volume.VolumeId,
            });
        }


        public async Task<IEnumerable<Ec2Volume>> ListVolumesAsync()
        {
            var volumes = new List<Ec2Volume>();
            foreach (var volume in await this.GetAttachedVolumesAsync())
            {
                var attachment = volume.Attachments.FirstOrDefault(x => x.InstanceId == this.InstanceId);
                if (attachment != null && mountPoints.Contains(attachment.Device))
                {
                    var tag = volume.Tags.FirstOrDefault(x => x.Key == "VolumeName");
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
            await this.Client.CreateTagsAsync(new CreateTagsRequest()
            {
                Resources = new List<string>() { this.InstanceId },
                Tags = new List<Tag>()
                {
                    new Tag() { Key = "CreatedByEc2Manager", Value = "true" },
                    new Tag() { Key = "Name", Value = this.Name },
                    new Tag() { Key = "UniqueKey", Value = this.uniqueKey },
                },
            });

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
                await this.Client.RebootInstancesAsync(new RebootInstancesRequest()
                {
                    InstanceIds = new List<string>() { this.InstanceId },
                });
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
                KeyName = this.privateKeyPair.KeyName,
                SecurityGroups = new List<string>() { this.SecurityGroupName },
            };
            if (!string.IsNullOrWhiteSpace(this.Specification.AvailabilityZone))
            {
                launchSpecification.Placement = new SpotPlacement() { AvailabilityZone = this.Specification.AvailabilityZone };
            }

            var spotResponse = await this.Client.RequestSpotInstancesAsync(new RequestSpotInstancesRequest()
            {
                InstanceCount = 1,
                SpotPrice = this.Specification.SpotBidPrice.ToString(),
                LaunchSpecification = launchSpecification,
            });
            this.bidRequestId = spotResponse.SpotInstanceRequests[0].SpotInstanceRequestId;

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

            await this.Client.CancelSpotInstanceRequestsAsync(new CancelSpotInstanceRequestsRequest()
            {
                SpotInstanceRequestIds = new List<string>() { this.bidRequestId },
            });

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
                KeyName = this.privateKeyPair.KeyName,
                SecurityGroups = new List<string>() { this.SecurityGroupName },
            };
            if (!string.IsNullOrWhiteSpace(this.Specification.AvailabilityZone))
            {
                runInstanceRequest.Placement = new Placement() { AvailabilityZone = this.Specification.AvailabilityZone };
            }

            var runResponse = await this.Client.RunInstancesAsync(runInstanceRequest);
            var instances = runResponse.Reservation.Instances;
            this.InstanceId = instances[0].InstanceId;
            this.Logger.Log("New instance created. Instance ID: {0}", this.InstanceId);

            await this.SetupInstanceAsync(cancellationToken);
        }

        private async Task TerminateAsync()
        {
            if (this.InstanceId == null)
                return;

            this.Logger.Log("Terminating instance");
            await this.Client.TerminateInstancesAsync(new TerminateInstancesRequest()
            {
                InstanceIds = new List<string>() { this.InstanceId },
            });

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
                SpotInstanceRequestIds = new List<string>() { spotInstanceRequestId },
            };

            while (instanceId == null)
            {
                token.ThrowIfCancellationRequested();

                var bidState = (await this.Client.DescribeSpotInstanceRequestsAsync(bidStateRequest)).SpotInstanceRequests[0];
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

                this.InstanceState = (await this.DescribeInstanceAsync()).State.Name;

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
                this.privateKeyPair = await this.EnsureKeyPairCreatedAsync();
                token.ThrowIfCancellationRequested();
            }
            catch (Exception e)
            {
                exception = e;
            }
            if (exception != null)
            {
                this.Logger.Log("Error ensuring key pair created: {0}. Performing rollback", exception.Message);
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

                this.PublicIp = (await this.DescribeInstanceAsync()).PublicIpAddress;
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
                await this.DeleteSecurityGroupAsync();
                throw exception;
            }

            // We need to know this to create volumes
            if (this.Specification.AvailabilityZone == null)
                this.Specification.AvailabilityZone = (await this.DescribeInstanceAsync()).Placement.AvailabilityZone;

            this.Logger.Log("Instance has been created");
        }

        private void Reconnect(Instance runningInstance)
        {
            this.Name = runningInstance.Tags.First(x => x.Key == "Name").Value;

            this.PublicIp = runningInstance.PublicIpAddress;
            this.PublicIp = string.IsNullOrWhiteSpace(this.PublicIp) ? null : this.PublicIp;

            this.InstanceState = runningInstance.State.Name;

            var uniqueKey = runningInstance.Tags.FirstOrDefault(x => x.Key == "UniqueKey");
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
            var groupIds = instanceStatus.SecurityGroups;
            // This excludes volumes attached to other machines as well
            var volumes = (await this.GetAttachedVolumesAsync())
                .Where(x => x.Attachments.Count == 1)
                .Where(x => x.Tags.Any(y => y.Key == "CreatedByEc2Manager"))
                .Select(x => x.VolumeId).ToList();

            if (volumes.Count > 0)
            {
                // Detach the volumes in parallel, since it takes a nice long time
                this.Logger.Log("Found uniquely attached volumes: {0}", string.Join(", ", volumes));
                await Task.WhenAll(volumes.Select(volume => new Ec2Volume(this, volume).DeleteAsync()));
            }

            await this.TerminateAsync();

            // This has to be set after the instance has been terminated
            var allInstances = (await this.Client.DescribeInstancesAsync(new DescribeInstancesRequest())).Reservations
                .Where(x => x.Instances.All(y => y.State.Name != "terminated")).ToArray();

            var usedGroupIds = allInstances.SelectMany(x => x.Groups).Distinct();
            this.Logger.Log("Found security groups uniquely associated with instance: {0}", string.Join(", ", groupIds.Except(usedGroupIds)));

            foreach (var groupId in groupIds.Except(usedGroupIds))
            {
                await this.DeleteSecurityGroupAsync();
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
