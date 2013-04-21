using Caliburn.Micro;
using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Ec2Manager.Classes;
using Microsoft.Win32;
using System.IO;

namespace Ec2Manager.ViewModels
{
    [Export]
    [PartCreationPolicy(CreationPolicy.NonShared)]
    public class InstanceViewModel : Conductor<IScreen>.Collection.OneActive
    {
        private static readonly VolumeType[] volumeTypes = new[]
            {
                new VolumeType("theSnapshotName", "Left 4 Dead 2"),
                VolumeType.CustomSnapshot("Custom Snapshot"),
                VolumeType.CustomVolume("Custom Volume"),
            };

        public Ec2Manager Manager { get; private set; }
        public InstanceClient Client { get; private set; }

        private Logger logger;

        public VolumeType[] VolumeTypes
        {
            get { return volumeTypes; }
        }
        private VolumeType selectedVolumeType = volumeTypes[0];
        public VolumeType SelectedVolumeType
        {
            get { return this.selectedVolumeType; }
            set
            {
                this.selectedVolumeType = value;
                this.NotifyOfPropertyChange();
                this.NotifyOfPropertyChange(() => CanMountVolume);
            }
        }

        private string customVolumeSnapshotId;
        public string CustomVolumeSnapshotId
        {
            get { return this.customVolumeSnapshotId; }
            set
            {
                this.customVolumeSnapshotId = value;
                this.NotifyOfPropertyChange();
                this.NotifyOfPropertyChange(() => CanMountVolume);
            }
        }

        [ImportingConstructor]
        public InstanceViewModel(InstanceDetailsViewModel instanceDetailsModel, Logger logger)
        {
            this.logger = logger;
            instanceDetailsModel.Logger = logger;

            this.ActivateItem(instanceDetailsModel);
        }

        public async Task Setup(Ec2Manager manager, string instanceAmi, string instanceSize, string loginAs, string availabilityZone)
        {
            this.Manager = manager;

            this.DisplayName = this.Manager.Name;
            this.Manager.DefaultLogger = this.logger;

            this.Manager.Bind(s => s.InstanceState, (o, e) =>
                {
                    this.NotifyOfPropertyChange(() => CanTerminate);
                    this.NotifyOfPropertyChange(() => CanMountVolume);
                    this.NotifyOfPropertyChange(() => CanSavePrivateKey);
                });

            await this.Manager.CreateAsync(instanceAmi, instanceSize, availabilityZone);
            this.Client = new InstanceClient(this.Manager.PublicIp, loginAs, this.Manager.PrivateKey);
            await this.Client.ConnectAsync(this.logger);
            this.NotifyOfPropertyChange(() => CanMountVolume);
        }

        public bool CanTerminate
        {
            get { return this.Manager.InstanceState == "running"; }
        }

        public async void Terminate()
        {
            this.ActivateItem(this.Items[0]);
            await this.Manager.DestroyAsync();
            this.TryClose();
        }

        public bool CanMountVolume
        {
            get
            {
                return this.Manager.InstanceState == "running" && this.Client != null &&
                    (!this.selectedVolumeType.IsCustom || !string.IsNullOrWhiteSpace(this.CustomVolumeSnapshotId));
            }
        }
        public async void MountVolume()
        {
            var volumeViewModel = IoC.Get<VolumeViewModel>();
            bool volumeNotSnapshot = false;
            string volumeId;

            if (this.SelectedVolumeType.IsCustomVolume)
            {
                volumeNotSnapshot = true;
                volumeId = this.CustomVolumeSnapshotId;
            }
            else if (this.SelectedVolumeType.IsCustomSnapshot)
            {
                volumeId = this.CustomVolumeSnapshotId;
            }
            else
            {
                volumeId = this.SelectedVolumeType.SnapshotId;
            }

            this.ActivateItem(volumeViewModel);

            await volumeViewModel.Setup(this.Manager, this.Client, this.SelectedVolumeType.Name, volumeId, volumeNotSnapshot);
        }

        public bool CanSavePrivateKey
        {
            get { return this.Manager.InstanceState == "running"; }
        }
        public void SavePrivateKey()
        {
            var dialog = new SaveFileDialog()
            {
                FileName = "id_rsa",
            };
            var result = dialog.ShowDialog();
            if (result == true)
            {
                string fileName = dialog.FileName;
                File.WriteAllText(fileName, this.Manager.PrivateKey);
            }
        }
    }

    public class VolumeType
    {
        public string SnapshotId { get; set; }
        public string Name { get; set; }
        public bool IsCustomVolume { get; set; }
        public bool IsCustomSnapshot { get; set; }
        public bool IsCustom
        {
            get { return this.IsCustomSnapshot || this.IsCustomVolume; }
        }

        public VolumeType()
        {
        }

        public VolumeType(string snapshotId, string name)
        {
            this.SnapshotId = snapshotId;
            this.Name = name;
            this.IsCustomVolume = false;
            this.IsCustomSnapshot = false;
        }

        public static VolumeType CustomVolume(string name)
        {
            return new VolumeType()
            {
                Name = name,
                IsCustomSnapshot = false,
                IsCustomVolume = true,
            };
        }

        public static VolumeType CustomSnapshot(string name)
        {
            return new VolumeType()
            {
                Name = name,
                IsCustomSnapshot = true,
                IsCustomVolume = false,
            };
        }
    }
}
