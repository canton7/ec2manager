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
using System.Windows.Threading;
using System.Windows;
using System.Threading;

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
        private IWindowManager windowManager;

        public Logger Logger { get; private set; }
        private Config config;
        private DispatcherTimer uptimeTimer = new DispatcherTimer();

        private bool isSpotInstance = false;
        public bool IsSpotInstance
        {
            get { return this.isSpotInstance; }
            set
            {
                this.isSpotInstance = value;
                this.NotifyOfPropertyChange();
            }
        }

        private string uptime;
        public string Uptime
        {
            get { return this.uptime; }
            set
            {
                this.uptime = value;
                this.NotifyOfPropertyChange();
            }
        }

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

        private CancellationTokenSource cancelCts;
        public CancellationTokenSource CancelCts
        {
            get { return this.cancelCts; }
            private set
            {
                this.cancelCts = value;
                this.NotifyOfPropertyChange();
                this.NotifyOfPropertyChange(() => CanCancelAction);
            }
        }

        [ImportingConstructor]
        public InstanceViewModel(InstanceDetailsViewModel instanceDetailsModel, Logger logger, Config config, IWindowManager windowManager)
        {
            this.Logger = logger;
            this.config = config;
            this.windowManager = windowManager;
            this.uptimeTimer.Interval = TimeSpan.FromSeconds(3);
            this.uptimeTimer.Tick += (o, e) => this.Uptime = this.Client.GetUptime();

            instanceDetailsModel.Logger = logger;

            this.SelectedVolumeType = this.VolumeTypes[0];
            this.ActivateItem(instanceDetailsModel);
        }

        private void SetupWithManager()
        {
            this.DisplayName = this.Manager.Name;
            this.Manager.DefaultLogger = this.Logger;

            this.Manager.Bind(s => s.InstanceState, (o, e) =>
            {
                this.NotifyOfPropertyChange(() => CanTerminate);
                this.NotifyOfPropertyChange(() => CanMountVolume);
                this.NotifyOfPropertyChange(() => CanSavePrivateKey);
            });
        }

        private async Task RefreshVolumes()
        {
            var snapshots = await this.config.GetSnapshotConfigAsync();
            this.volumeTypes.Clear();
            this.volumeTypes.AddRange(snapshots);
            this.SelectedVolumeType = this.volumeTypes[0];
            this.NotifyOfPropertyChange(() => VolumeTypes);
        }

        public async Task SetupAsync(Ec2Manager manager, string instanceAmi, string instanceSize, string loginAs, string availabilityZone, double? spotBidAmount = null)
        {
            this.Manager = manager;
            this.SetupWithManager();
            this.IsSpotInstance = spotBidAmount.HasValue;

            this.CancelCts = new CancellationTokenSource();
            var createTask = Task.Run(async () =>
                {
                    await this.Manager.CreateAsync(instanceAmi, instanceSize, availabilityZone, spotBidAmount, this.CancelCts.Token);

                    this.Client = new InstanceClient(this.Manager.PublicIp, loginAs, this.Manager.PrivateKey);
                    this.Client.Bind(s => s.IsConnected, (o, e) => this.NotifyOfPropertyChange(() => CanMountVolume));

                    await this.Client.ConnectAsync(this.Logger);
                    this.config.SaveKeyAndUser(this.Manager.InstanceId, loginAs, this.Manager.PrivateKey);
                }, this.CancelCts.Token);

            try
            {
                await Task.WhenAll(createTask, this.RefreshVolumes());
                this.uptimeTimer.Start();
            }
            catch (OperationCanceledException)
            {
                this.TryClose();
            }
            catch (Exception e)
            {
                this.Logger.Log("Error occurred: {0}", e.Message);
                MessageBox.Show(Application.Current.MainWindow, "Error occurred: " + e.Message, "Error occurred", MessageBoxButton.OK, MessageBoxImage.Error);
                this.TryClose();
            }
            finally
            {
                this.CancelCts = null;
            }
        }

        public async Task ReconnectAsync(Ec2Manager manager)
        {
            this.Manager = manager;
            this.SetupWithManager();

            var reconnectTask = Task.Run(async () =>
                {
                    await this.Manager.ReconnectAsync(this.Logger);

                    Tuple<string, string> keyAndUser = null;
                    try
                    {
                        keyAndUser = this.config.RetrieveKeyAndUser(this.Manager.InstanceId);
                    }
                    catch (FileNotFoundException)
                    {
                        var reconnectDetails = IoC.Get<ReconnectDetailsViewModel>();
                        bool? result = false;
                        this.Invoke(() =>
                            {
                                result = this.windowManager.ShowDialog(reconnectDetails, settings: new Dictionary<string, object>()
                                {
                                    { "ResizeMode", ResizeMode.NoResize },
                                });
                            });

                        if (result.HasValue && result.Value)
                        {
                            keyAndUser = new Tuple<string, string>(reconnectDetails.PrivateKey, reconnectDetails.LoginAs);
                            this.config.SaveKeyAndUser(this.Manager.InstanceId, keyAndUser.Item2, keyAndUser.Item1);
                        }
                        else
                        {
                            throw new Exception("User cancelled");
                        }
                    }

                    this.Client = new InstanceClient(this.Manager.PublicIp, keyAndUser.Item2, keyAndUser.Item1);
                    this.Client.Bind(s => s.IsConnected, (o, e) => this.NotifyOfPropertyChange(() => CanMountVolume));
                    await this.Client.ConnectAsync(this.Logger);

                    foreach (var volume in await this.Manager.GetAttachedVolumeDescriptionsAsync())
                    {
                        var volumeViewModel = IoC.Get<VolumeViewModel>();
                        this.ActivateItem(volumeViewModel);
                        volumeViewModel.Reconnect(this.Manager, this.Client, volume.VolumeName, volume.VolumeId, volume.MountPointDir);
                    }
                });

            try
            {
                await Task.WhenAll(reconnectTask, this.RefreshVolumes());
            }
            catch (Exception e)
            {
                this.Logger.Log("Error occurred: {0}", e.Message);
                MessageBox.Show(Application.Current.MainWindow, "Error occurred: " + e.Message, "Error occurred", MessageBoxButton.OK, MessageBoxImage.Error);
                this.TryClose();
                return;
            }

            this.uptimeTimer.Start();
        }

        public bool CanCancelAction
        {
            get { return this.CancelCts != null && !this.CancelCts.IsCancellationRequested; }
        }
        public void CancelAction()
        {
            if (this.CancelCts == null)
                return;

            this.Logger.Log("Starting to cancel operation");
            this.CancelCts.Cancel();
            this.NotifyOfPropertyChange(() => CanCancelAction);
        }

        public bool CanTerminate
        {
            get { return this.Manager != null && this.Manager.InstanceState == "running"; }
        }

        public async void Terminate()
        {
            this.ActivateItem(this.Items[0]);
            this.uptimeTimer.Stop();
            await this.Manager.DestroyAsync();
            this.TryClose();
        }

        public bool CanMountVolume
        {
            get
            {
                return this.Manager != null &&
                    this.Manager.InstanceState == "running" && this.Client != null &&
                    (this.SelectedVolumeType.IsCustom == true || this.SelectedVolumeType.SnapshotId != null) &&
                    (!this.selectedVolumeType.IsCustom || !string.IsNullOrWhiteSpace(this.CustomVolumeSnapshotId)) &&
                    this.Client.IsConnected;
            }
        }

        public async void MountVolume()
        {
            var volumeViewModel = IoC.Get<VolumeViewModel>();
            string volumeId;
            string volumeName;

            if (this.SelectedVolumeType.IsCustom)
            {
                volumeId = this.CustomVolumeSnapshotId;
                volumeName = this.CustomVolumeSnapshotId;
            }
            else
            {
                volumeId = this.SelectedVolumeType.SnapshotId;
                volumeName = this.selectedVolumeType.Name;
            }

            this.ActivateItem(volumeViewModel);

            try
            {
                await volumeViewModel.SetupAsync(this.Manager, this.Client, volumeName, volumeId);
            }
            catch (OperationCanceledException)
            {
                this.Logger.Log("Volume mounting cancelled");
                volumeViewModel.TryClose();
            }
            catch (Exception e)
            {
                this.Logger.Log("Error occurred: {0}", e.Message);
                MessageBox.Show(Application.Current.MainWindow, "Error occurred: " + e.Message, "Error occurred", MessageBoxButton.OK, MessageBoxImage.Error);
                volumeViewModel.TryClose();
            }
        }

        public bool CanSavePrivateKey
        {
            get { return this.Manager != null && this.Manager.InstanceState == "running"; }
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
