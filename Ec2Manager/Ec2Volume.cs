﻿using Amazon.EC2;
using Amazon.EC2.Model;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Ec2Manager.Ec2Manager
{
    public class Ec2Volume
    {
        public Ec2Instance Instance { get; private set; }
        public AmazonEC2Client Client
        {
            get { return this.Instance.Client; }
        }
        private string source;
        public string Name { get; private set; }
        public ILogger Logger;
        public bool IsSetup { get; private set; }

        public string VolumeId { get; private set; }

        public string Device { get; private set; }
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

        // Connection to existing
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

        #region "Until" methods

        private async Task UntilVolumeStateAsync(string volumeId, string state, CancellationToken? cancellationToken = null)
        {
            CancellationToken token = cancellationToken.HasValue ? cancellationToken.Value : new CancellationToken();

            var describeVolumesRequest = new DescribeVolumesRequest()
            {
                VolumeId = new List<string>() { volumeId },
            };

            while (true)
            {
                token.ThrowIfCancellationRequested();

                var describeVolumesResponse = await this.Client.RequestAsync(s => s.DescribeVolumes(describeVolumesRequest));
                var status = describeVolumesResponse.DescribeVolumesResult.Volume.FirstOrDefault(x => x.VolumeId == volumeId).Status;

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
                VolumeId = new List<string>() { this.VolumeId },
            };

            while (true)
            {
                token.ThrowIfCancellationRequested();
                var describeVolumesResponse = await this.Client.RequestAsync(s => s.DescribeVolumes(describeVolumesRequest));
                var volume = describeVolumesResponse.DescribeVolumesResult.Volume
                    .FirstOrDefault(x => x.VolumeId == this.VolumeId);

                if (volume != null && ((volume.Attachment.Count == 0 && allowNone) || volume.Attachment.FirstOrDefault(x => x.InstanceId == this.Instance.InstanceId).Status == state))
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
                SnapshotId = new List<string>() { snapshotId },
            };

            while (true)
            {
                token.ThrowIfCancellationRequested();

                var describeSnapshotsResponse = await this.Client.RequestAsync(s => s.DescribeSnapshots(describeSnapshotsRequest));
                var status = describeSnapshotsResponse.DescribeSnapshotsResult.Snapshot.FirstOrDefault(x => x.SnapshotId == snapshotId).Status;

                if (status == state)
                    break;
                else
                    await Task.Delay(1000, token);
            }
        }

        #endregion

        #region Snapshot Interaction

        private async Task<string> CreateVolumeFromSnapshotAsync(string snapshotId, CancellationToken? cancellationToken = null)
        {
            CancellationToken token = cancellationToken.HasValue ? cancellationToken.Value : new CancellationToken();

            this.Logger.Log("Starting device mount process. Snapshot ID: {0}", snapshotId);

            this.Logger.Log("Creating EBS volume based on snapshot");
            var createVolumeResponse = await this.Client.RequestAsync(s => s.CreateVolume(new CreateVolumeRequest()
            {
                SnapshotId = snapshotId,
                VolumeType = "standard",
                AvailabilityZone = this.Instance.Specification.AvailabilityZone,
            }));
            var volumeId = createVolumeResponse.CreateVolumeResult.Volume.VolumeId;
            this.Logger.Log("Volume ID {0} created", volumeId);

            this.Logger.Log("Tagging volume, so we know we can remove it later");
            var name = this.Name == null ? "Unnamed" : this.Instance.Name + " - " + this.Name;
            await this.Client.RequestAsync(s => s.CreateTags(new CreateTagsRequest()
            {
                ResourceId = new List<string>() { volumeId },
                Tag = new List<Tag>()
                {
                    new Tag() { Key = "CreatedByEc2Manager", Value = "true" },
                    new Tag() { Key = "Name", Value = name },
                    new Tag() { Key = "VolumeName", Value = this.Name },
                },
            }));

            this.Logger.Log("Waiting for volume to reach the 'available' state");
            await this.UntilVolumeStateAsync(volumeId, "available", token);

            return volumeId;
        }

        public async Task<string> CreateSnapshotAsync(string snapshotName, string snapshotDescription, bool isPublic, CancellationToken? cancellationToken = null)
        {
            CancellationToken token = cancellationToken.HasValue ? cancellationToken.Value : new CancellationToken();

            this.Logger.Log("Starting to create snapshot");

            var response = await this.Client.RequestAsync(s => s.CreateSnapshot(new CreateSnapshotRequest()
            {
                VolumeId = this.VolumeId,
                Description = snapshotDescription,
            }));
            var snapshotId = response.CreateSnapshotResult.Snapshot.SnapshotId;

            this.Logger.Log("Waiting for snapshot to reach the 'completed' state");
            await this.UntilSnapshotStateAsync(snapshotId, "completed", cancellationToken);
            this.Logger.Log("Snapshot {0} created", snapshotId);

            this.Logger.Log("Tagging snapshot");
            await this.Client.RequestAsync(s => s.CreateTags(new CreateTagsRequest()
            {
                ResourceId = new List<string>() { snapshotId },
                Tag = new List<Tag>()
                {
                    new Tag() { Key = "Name", Value = snapshotName },
                },
            }));

            if (isPublic)
            {
                this.Logger.Log("Setting permissions to public");
                await this.Client.RequestAsync(s => s.ModifySnapshotAttribute(new ModifySnapshotAttributeRequest()
                {
                    SnapshotId = snapshotId,
                    OperationType = "add",
                    Attribute = "createVolumePermission",
                    UserGroup = new List<string>() { "all" },
                }));
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

            CancellationToken token = cancellationToken.HasValue ? cancellationToken.Value : new CancellationToken();
            bool weCreatedVolume = false;

            if (this.source.StartsWith("snap-"))
            {
                weCreatedVolume = true;
                try
                {
                    this.VolumeId = await this.CreateVolumeFromSnapshotAsync(this.source, token);
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
                await sshClient.MountAndSetupDeviceAsync(this.Device, this.MountPoint, this.Logger);
                token.ThrowIfCancellationRequested();

                this.Logger.Log("Retriving port settings");
                var portSettings = sshClient.GetPortDescriptions(this.MountPoint, this.Logger).ToArray();
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

        public async Task DeleteAsync()
        {
            await this.Instance.DetachVolumeAsync(this);

            this.Logger.Log("Waiting until volume {0} becomes available", this.VolumeId);
            await this.UntilVolumeAttachedStateAsync("available");

            this.Logger.Log("Deleting volume {0}", this.VolumeId);
            await this.Client.RequestAsync(s => s.DeleteVolume(new DeleteVolumeRequest()
            {
                VolumeId = this.VolumeId,
            }));

            this.Logger.Log("Volume {0} deleted", this.VolumeId);
        }
    }
}
