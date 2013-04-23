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
using Ec2Manager.Configuration;

namespace Ec2Manager.ViewModels
{
    [Export]
    [PartCreationPolicy(CreationPolicy.NonShared)]
    public class InstanceViewModel : Conductor<IScreen>.Collection.OneActive
    {
        private static readonly List<VolumeType> defaultVolumeTypes = new List<VolumeType>
            {
                //new VolumeType("theSnapshotName", "Left 4 Dead 2"),
                VolumeType.Custom("Custom Snapshot or Volume"),
            };

        public Ec2Manager Manager { get; private set; }
        public InstanceClient Client { get; private set; }

        private Logger logger;
        private Config config;

        private List<VolumeType> volumeTypes = new List<VolumeType>() { new VolumeType(null, "Loading...") };
        public List<VolumeType> VolumeTypes
        {
            get { return volumeTypes.Concat(defaultVolumeTypes).ToList(); }
        }
        private VolumeType selectedVolumeType;
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
        public InstanceViewModel(InstanceDetailsViewModel instanceDetailsModel, Logger logger, Config config)
        {
            this.logger = logger;
            this.config = config;

            instanceDetailsModel.Logger = logger;

            this.SelectedVolumeType = this.VolumeTypes[0];
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

            var createTask = Task.Run(async () =>
                {
                    await this.Manager.CreateAsync(instanceAmi, instanceSize, availabilityZone);
                    this.Client = new InstanceClient(this.Manager.PublicIp, loginAs, this.Manager.PrivateKey);
                    await this.Client.ConnectAsync(this.logger);
                    this.NotifyOfPropertyChange(() => CanMountVolume);
                });

            var volumesTask = Task.Run(async () =>
                {
                    var snapshots = await this.config.GetSnapshotConfigAsync();
                    this.volumeTypes.Clear();
                    this.volumeTypes.AddRange(snapshots);
                    this.SelectedVolumeType = this.volumeTypes[0];
                    this.NotifyOfPropertyChange(() => VolumeTypes);
                });

            await Task.WhenAll(createTask, volumesTask);
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
                    (this.SelectedVolumeType.IsCustom == true || this.SelectedVolumeType.SnapshotId != null) &&
                    (!this.selectedVolumeType.IsCustom || !string.IsNullOrWhiteSpace(this.CustomVolumeSnapshotId));
            }
        }
        public async void MountVolume()
        {
            var volumeViewModel = IoC.Get<VolumeViewModel>();
            string volumeId;

            if (this.SelectedVolumeType.IsCustom)
            {
                volumeId = this.CustomVolumeSnapshotId;
            }
            else
            {
                volumeId = this.SelectedVolumeType.SnapshotId;
            }

            this.ActivateItem(volumeViewModel);

            await volumeViewModel.Setup(this.Manager, this.Client, this.SelectedVolumeType.Name, volumeId);
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
}
