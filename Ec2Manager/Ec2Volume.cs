using Amazon.EC2;
using Amazon.EC2.Model;
using Caliburn.Micro;
using Ec2Manager.Utilities;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Ec2Manager.Ec2Manager
{
    public class Ec2Volume : PropertyChangedBase
    {
        public Ec2Instance Instance { get; private set; }
        public AmazonEC2Client Client
        {
            get { return this.Instance.Client; }
        }
        private string source;
        private int? sizeGb; // Only used when creating a new one from scratch
        public string Name { get; private set; }
        public ILogger Logger;
        public bool IsSetup { get; private set; }

        public string VolumeId { get; private set; }

        private string device;
        public string Device
        {
            get { return this.device; }
            private set
            {
                this.device = value;
                this.NotifyOfPropertyChange();
                this.NotifyOfPropertyChange(() => MountPoint);
            }
        }

        public string MountPoint
        {
            get { return Path.GetFileName(this.Device); }
        }

        // New creation
        public Ec2Volume(Ec2Instance instance, string source, string name)
        {
            this.Instance = instance;
            this.source = source;
            this.Name = name;
            this.Logger = instance.Logger;

            this.IsSetup = false;
        }

        // Connection to existing. Currently only used for deletion
        public Ec2Volume(Ec2Instance instance, string volumeId)
        {
            this.Instance = instance;
            this.VolumeId = volumeId;

            // TODO: Get name and mountPoint source from Ec2

            this.Logger = new StubLogger();
            this.IsSetup = true;
        }

        // Shortcut to the above, if the information is already available
        public Ec2Volume(Ec2Instance instance, string volumeId, string name, string device)
        {
            this.Instance = instance;
            this.VolumeId = volumeId;
            this.Name = name;
            this.Device = device;

            this.Logger = instance.Logger;
            this.IsSetup = true;
        }

        // Create a new empty volume, not from snapshot
        public Ec2Volume(Ec2Instance instance, string name, int sizeGb)
        {
            this.Instance = instance;
            this.Name = name;
            this.sizeGb = sizeGb;
            this.Logger = instance.Logger;

            this.IsSetup = false;
        }

        #region "Until" methods

        private async Task UntilVolumeStateAsync(string volumeId, string state, CancellationToken? cancellationToken = null)
        {
            CancellationToken token = cancellationToken.HasValue ? cancellationToken.Value : new CancellationToken();

            var describeVolumesRequest = new DescribeVolumesRequest()
            {
                VolumeIds = new List<string>() { volumeId },
            };

            while (true)
            {
                token.ThrowIfCancellationRequested();

                var describeVolumesResponse = await this.Client.DescribeVolumesAsync(describeVolumesRequest);
                var status = describeVolumesResponse.Volumes.FirstOrDefault(x => x.VolumeId == volumeId).State;

                if (status == state)
                    break;
                else
                    await Task.Delay(1000, token);
            }
        }

        private async Task UntilVolumeAttachedStateAsync(string state, bool allowNone = true, CancellationToken? cancellationToken = null)
        {
            CancellationToken token = cancellationToken.HasValue ? cancellationToken.Value : new CancellationToken();

            var describeVolumesRequest = new DescribeVolumesRequest()
            {
                VolumeIds = new List<string>() { this.VolumeId },
            };

            while (true)
            {
                token.ThrowIfCancellationRequested();
                var describeVolumesResponse = await this.Client.DescribeVolumesAsync(describeVolumesRequest);
                var volume = describeVolumesResponse.Volumes
                    .FirstOrDefault(x => x.VolumeId == this.VolumeId);

                if (volume != null && ((volume.Attachments.Count == 0 && allowNone) || volume.Attachments.FirstOrDefault(x => x.InstanceId == this.Instance.InstanceId).State == state))
                    break;
                else
                    await Task.Delay(1000, token);
            }
        }

        private async Task UntilSnapshotStateAsync(string snapshotId, string state, CancellationToken? cancellationToken = null)
        {
            CancellationToken token = cancellationToken.HasValue ? cancellationToken.Value : new CancellationToken();

            var describeSnapshotsRequest = new DescribeSnapshotsRequest()
            {
                SnapshotIds = new List<string>() { snapshotId },
            };

            while (true)
            {
                token.ThrowIfCancellationRequested();

                var describeSnapshotsResponse = await this.Client.DescribeSnapshotsAsync(describeSnapshotsRequest);
                var status = describeSnapshotsResponse.Snapshots.FirstOrDefault(x => x.SnapshotId == snapshotId).State;

                if (status == state)
                    break;
                else
                    await Task.Delay(1000, token);
            }
        }

        #endregion

        #region Snapshot Interaction

        /// <param name="snapshotId">If null, create from scratch</param>
        private async Task<string> CreateVolumeAsync(string snapshotId = null, int? size = null, CancellationToken? cancellationToken = null)
        {
            CancellationToken token = cancellationToken.HasValue ? cancellationToken.Value : new CancellationToken();

            if (string.IsNullOrEmpty(snapshotId))
                this.Logger.Log("Starting device mount process, with a new volume");
            else
                this.Logger.Log("Starting device mount process. Snapshot ID: {0}", snapshotId);

            var request = new CreateVolumeRequest()
            {
                SnapshotId = string.IsNullOrEmpty(snapshotId) ? null : snapshotId,    // Null is the default value here
                VolumeType = "standard",
                AvailabilityZone = this.Instance.Specification.AvailabilityZone,
            };
            if (size != null)
                request.Size = size.Value;

            var createVolumeResponse = await this.Client.CreateVolumeAsync(request);
            var volumeId = createVolumeResponse.Volume.VolumeId;
            this.Logger.Log("Volume ID {0} created", volumeId);

            this.Logger.Log("Tagging volume, so we know we can remove it later");
            var name = this.Name == null ? "Unnamed" : this.Instance.Name + " - " + this.Name;

            var tags = new List<Tag>()
                {
                    new Tag() { Key = "CreatedByEc2Manager", Value = "true" },
                    new Tag() { Key = "Name", Value = name },
                    new Tag() { Key = "VolumeName", Value = this.Name },
                };

            await this.Client.CreateTagsAsync(new CreateTagsRequest()
            {
                Resources = new List<string>() { volumeId },
                Tags = tags,
            });

            this.Logger.Log("Waiting for volume to reach the 'available' state");
            await this.UntilVolumeStateAsync(volumeId, "available", token);

            return volumeId;
        }

        public async Task<Tuple<string, string>> GetSourceSnapshotNameDescriptionAsync()
        {
            this.Logger.Log("Retrieving name and description of sourse snapshot");

            var snapshotId = (await this.Client.DescribeVolumesAsync(new DescribeVolumesRequest()
            {
                VolumeIds = new List<string>() { this.VolumeId },
            })).Volumes[0].SnapshotId;

            if (string.IsNullOrWhiteSpace(snapshotId))
            {
                this.Logger.Log("No source snapshot found");
                return new Tuple<string, string>(null, null);
            }

            var snapshot = (await this.Client.DescribeSnapshotsAsync(new DescribeSnapshotsRequest()
            {
                SnapshotIds = new List<string>() { snapshotId },
            })).Snapshots.FirstOrDefault();

            if (snapshot == null)
            {
                this.Logger.Log("Could not find source snapshot with ID " + snapshotId);
                return new Tuple<string, string>(null, null);
            }

            var nameTag = snapshot.Tags.FirstOrDefault(x => x.Key == "Name");
            var descriptionTag = snapshot.Description;

            return Tuple.Create(nameTag == null ? null : nameTag.Value, descriptionTag);
        }

        public async Task<bool> AnySnapshotsExistWithName(string name)
        {
            var result = await this.Client.DescribeSnapshotsAsync(new DescribeSnapshotsRequest()
            {
                Filters = new List<Filter>() { new Filter() { Name = "tag:Name", Values = new List<string>() { name } } },
                OwnerIds = new List<string>(){ "self" },
            });

            return result.Snapshots.Any();
        }

        public async Task<string> CreateSnapshotAsync(string snapshotName, string snapshotDescription, bool isPublic, CancellationToken? cancellationToken = null)
        {
            CancellationToken token = cancellationToken.HasValue ? cancellationToken.Value : new CancellationToken();

            this.Logger.Log("Starting to create snapshot");

            var response = await this.Client.CreateSnapshotAsync(new CreateSnapshotRequest()
            {
                VolumeId = this.VolumeId,
                Description = snapshotDescription,
            });
            var snapshotId = response.Snapshot.SnapshotId;

            this.Logger.Log("Waiting for snapshot to reach the 'completed' state");
            await this.UntilSnapshotStateAsync(snapshotId, "completed", cancellationToken);
            this.Logger.Log("Snapshot {0} created", snapshotId);

            this.Logger.Log("Tagging snapshot");
            await this.Client.CreateTagsAsync(new CreateTagsRequest()
            {
                Resources = new List<string>() { snapshotId },
                Tags = new List<Tag>()
                {
                    new Tag() { Key = "Name", Value = snapshotName },
                },
            });

            if (isPublic)
            {
                this.Logger.Log("Setting permissions to public");
                await this.Client.ModifySnapshotAttributeAsync(new ModifySnapshotAttributeRequest()
                {
                    SnapshotId = snapshotId,
                    OperationType = "add",
                    Attribute = "createVolumePermission",
                    GroupNames = new List<string>() { "all" },
                });
            };

            this.Logger.Log("Done");

            return snapshotId;
        }

        #endregion

        public async Task SetupAsync(IMachineInteractionProvider sshClient, CancellationToken? cancellationToken = null)
        {
            if (this.IsSetup)
            {
                this.Logger.Log("Already set up. Nothing to do");

                // TODO: More to be done here?
                return;
            }

            if (string.IsNullOrEmpty(this.source) && this.sizeGb.HasValue)
            {
                await this.SetupEmptyVolumeAsync(sshClient, sizeGb.Value, cancellationToken);
            }
            else if (!string.IsNullOrEmpty(this.source))
            {
                await this.SetupVolumeFromSourceAsync(sshClient, cancellationToken);
            }
            else
            {
                throw new Exception("Neither source not size specified. Don't know how to create volume");
            }
        }

        public async Task SetupVolumeFromSourceAsync(IMachineInteractionProvider sshClient, CancellationToken? cancellationToken = null)
        {
            CancellationToken token = cancellationToken.HasValue ? cancellationToken.Value : new CancellationToken();
            bool weCreatedVolume = false;

            if (this.source.StartsWith("snap-"))
            {
                weCreatedVolume = true;
                try
                {
                    this.VolumeId = await this.CreateVolumeAsync(snapshotId: this.source, cancellationToken: token);
                    token.ThrowIfCancellationRequested();
                }
                catch (Exception e)
                {
                    this.Logger.Log("Error creating volume from snapshot: {0}", e.Message);
                    throw;
                }
            }
            else if (this.source.StartsWith("vol-"))
            {
                this.VolumeId = this.source;
            }
            else
            {
                throw new Exception("Volume ID must start with vol- or snap-");
            }

            Exception exception = null;
            try
            {
                this.Logger.Log("Waiting for volume to reach the 'attached' state");
                this.Device = await this.Instance.AttachVolumeAsync(this);
                await this.UntilVolumeAttachedStateAsync("attached", cancellationToken: token);
                token.ThrowIfCancellationRequested();

                this.Logger.Log("Mounting and setting up device");
                await sshClient.MountDeviceAsync(this.Device, this.MountPoint, this.Logger, cancellationToken);
                await sshClient.SetupDeviceAsync(this.Device, this.MountPoint, this.Logger);
                token.ThrowIfCancellationRequested();

                this.Logger.Log("Retriving port settings");
                var portSettings = (await sshClient.GetPortDescriptionsAsync(this.MountPoint, this.Logger, cancellationToken)).ToArray();
                token.ThrowIfCancellationRequested();

                await this.Instance.AuthorizeIngressAsync(portSettings);
                token.ThrowIfCancellationRequested();
            }
            catch (Exception e)
            {
                exception = e;
            }
            if (exception != null)
            {
                this.Logger.Log("Error performing the last operation: {0}. Rolling back", exception.Message);
                if (weCreatedVolume)
                    await this.DeleteAsync();
                throw exception;
            }

            this.IsSetup = true;

            this.Logger.Log("Volume successfully mounted");
        }

        private async Task SetupEmptyVolumeAsync(IMachineInteractionProvider sshClient, int size, CancellationToken? cancellationToken = null)
        {
            CancellationToken token = cancellationToken.HasValue ? cancellationToken.Value : new CancellationToken();

            try
            {
                this.VolumeId = await this.CreateVolumeAsync(size: size, cancellationToken: token);
                token.ThrowIfCancellationRequested();  
            }
            catch (Exception e)
            {
                this.Logger.Log("Error creating new volume: {0}", e.Message);
                throw;
            }

            Exception exception = null;
            try
            {
                this.Logger.Log("Waiting for volume to reach the 'attached' state");
                this.Device = await this.Instance.AttachVolumeAsync(this);
                await this.UntilVolumeAttachedStateAsync("attached", cancellationToken: token);
                token.ThrowIfCancellationRequested();

                this.Logger.Log("Creating filesystem");
                await sshClient.SetupFilesystemAsync(this.Device, this.Logger);
                token.ThrowIfCancellationRequested();

                this.Logger.Log("Mounting device");
                await sshClient.MountDeviceAsync(this.Device, this.MountPoint, this.Logger, cancellationToken);
                token.ThrowIfCancellationRequested();
            }
            catch (Exception e)
            {
                exception = e;
            }
            if (exception != null)
            {
                await this.DeleteAsync();
                throw exception;
            }

            this.IsSetup = true;

            this.Logger.Log("Volume successfully created");
        }

        public async Task DeleteAsync()
        {
            await this.Instance.DetachVolumeAsync(this);

            this.Logger.Log("Waiting until volume {0} becomes available", this.VolumeId);
            await this.UntilVolumeAttachedStateAsync("available");

            this.Logger.Log("Deleting volume {0}", this.VolumeId);
            await this.Client.DeleteVolumeAsync(new DeleteVolumeRequest()
            {
                VolumeId = this.VolumeId,
            });

            this.Logger.Log("Volume {0} deleted", this.VolumeId);
        }
    }
}
