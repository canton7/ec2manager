using Caliburn.Micro;
using System;
using System.Collections.Generic;
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
using Ec2Manager.Ec2Manager;
using System.Diagnostics;
using Ec2Manager.Utilities;
using System.Windows.Data;

namespace Ec2Manager.ViewModels
{
    public class InstanceViewModel : Conductor<IScreen>.Collection.OneActive
    {
        private static readonly List<VolumeType> defaultVolumeTypes = new List<VolumeType>
            {
                VolumeType.Custom("Custom Snapshot or Volume", new Friend(null, "Your Images")),
            };

        public Ec2Instance Instance { get; private set; }
        public InstanceClient Client { get; private set; }
        private Ec2Connection connection;
        private IVolumeViewModelFactory volumeViewModelFactory;
        private IWindowManager windowManager;

        public Logger Logger { get; private set; }
        private Config config;
        private System.Timers.Timer uptimeTimer;

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

        public BindableCollection<VolumeType> VolumeTypes { get; private set; }

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

        private string instanceState = "starting";
        public string InstanceState
        {
            get { return this.instanceState; }
            set
            {
                this.instanceState = value;
                this.NotifyOfPropertyChange();
                this.NotifyOfPropertyChange(() => CanMountVolume);
                this.NotifyOfPropertyChange(() => CanTerminate);
                this.NotifyOfPropertyChange(() => CanCreateVolume);
            }
        }

        public InstanceViewModel(InstanceDetailsViewModel instanceDetailsModel, Logger logger, Config config, Ec2Connection connection, IVolumeViewModelFactory volumeViewModelFactory, IWindowManager windowManager)
        {
            this.Logger = logger;
            this.config = config;
            this.connection = connection;
            this.volumeViewModelFactory = volumeViewModelFactory;
            this.windowManager = windowManager;
            this.uptimeTimer = new System.Timers.Timer();
            this.uptimeTimer.Elapsed += async (o, e) => 
                {
                    if (this.Client == null || !this.Client.IsConnected)
                        this.uptimeTimer.Stop();
                    else
                        this.Uptime = await this.Client.GetUptimeAsync(this.Logger);
                };
            this.uptimeTimer.AutoReset = true;
            this.uptimeTimer.Interval = 3000;

            instanceDetailsModel.Logger = logger;

            this.VolumeTypes = new BindableCollection<VolumeType>() { new VolumeType(null, "Loading...", null) };
            this.SelectedVolumeType = this.VolumeTypes[0];

            this.ActivateItem(instanceDetailsModel);
        }

        private void SetupWithInstance()
        {
            this.DisplayName = this.Instance.Name;
            this.Instance.Logger = this.Logger;

            System.Action update = () =>
                {
                    this.NotifyOfPropertyChange(() => CanTerminate);
                    this.NotifyOfPropertyChange(() => CanMountVolume);
                    this.NotifyOfPropertyChange(() => CanCreateVolume);
                    this.NotifyOfPropertyChange(() => CanSavePrivateKey);
                    this.NotifyOfPropertyChange(() => CanSavePuttyKey);
                    this.NotifyOfPropertyChange(() => CanLaunchPutty);
                };

            this.Instance.Bind(s => s.InstanceState, (o, e) => update());
            update();
        }

        private async Task RefreshVolumes()
        {
            var snapshots = await this.connection.CreateSnapshotBrowser().GetSnapshotsForFriendsAsync(this.config.Friends);
            this.VolumeTypes.Clear();
            if (snapshots.Any())
            {
                this.VolumeTypes.AddRange(snapshots);
            }
            this.VolumeTypes.AddRange(defaultVolumeTypes);
            this.SelectedVolumeType = this.VolumeTypes[0];
            this.NotifyOfPropertyChange(() => VolumeTypes);
        }

        private string CreatePuttyKey()
        {
            var keyLines = this.Client.Key.Split(new[] { "\n", "\r\n" }, StringSplitOptions.None);
            var keyBytes = System.Convert.FromBase64String(string.Join("", keyLines.Skip(1).Take(keyLines.Length - 2)));
            var rsaKey = RSAConverter.FromDERPrivateKey(keyBytes);
            rsaKey.Comment = "Ec2manager: " + this.Instance.InstanceId;
            return rsaKey.ToPuttyPrivateKey();
        }

        public async Task SetupAsync(Ec2Instance instance, string loginAs)
        {
            this.Instance = instance;
            this.SetupWithInstance();
            this.IsSpotInstance = instance.Specification.IsSpotInstance;

            this.Instance.Logger = this.Logger;

            this.CancelCts = new CancellationTokenSource();
            var createTask = Task.Run(async () =>
                {
                    await this.Instance.SetupAsync(this.CancelCts.Token);
                    this.InstanceState = "running";

                    this.Client = new InstanceClient(this.Instance.PublicIp, loginAs, this.Instance.PrivateKey);
                    this.Client.Bind(s => s.IsConnected, (o, e) =>
                        {
                            this.NotifyOfPropertyChange(() => CanMountVolume);
                            this.NotifyOfPropertyChange(() => CanCreateVolume);
                        });
                    
                    this.config.SaveKeyAndUser(this.Instance.InstanceId, loginAs, this.Instance.PrivateKey);

                    Exception exception = null;
                    try
                    {
                        // It takes them a little while to get going...
                        this.Logger.Log("Waiting for 10 seconds for instance to boot");
                        await Task.Delay(10000);
                        await this.Client.ConnectAsync(this.Logger);
                    }
                    catch (Exception e)
                    {
                        exception = e;
                    }
                    if (exception != null)
                    {
                        this.Logger.Log("The instance will now be terminated");
                        await this.Instance.DestroyAsync();
                    }

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

        public async Task ReconnectAsync(Ec2Instance instance)
        {
            this.Instance = instance;
            this.SetupWithInstance();

            this.Instance.Logger = this.Logger;

            this.CancelCts = new CancellationTokenSource();
            var reconnectTask = Task.Run(async () =>
                {
                    await this.Instance.SetupAsync();
                    this.InstanceState = "running";

                    Tuple<string, string> keyAndUser = null;
                    try
                    {
                        keyAndUser = this.config.RetrieveKeyAndUser(this.Instance.InstanceId);
                    }
                    catch (FileNotFoundException)
                    {
                        var result = this.windowManager.ShowDialog<ReconnectDetailsViewModel>(settings: new Dictionary<string, object>()
                                {
                                    { "ResizeMode", ResizeMode.NoResize },
                                });

                        if (result.Result.GetValueOrDefault())
                        {
                            keyAndUser = new Tuple<string, string>(result.VM.PrivateKey, result.VM.LoginAs);
                            this.config.SaveKeyAndUser(this.Instance.InstanceId, keyAndUser.Item2, keyAndUser.Item1);
                        }
                        else
                        {
                            throw new Exception("User cancelled");
                        }
                    }

                    this.Client = new InstanceClient(this.Instance.PublicIp, keyAndUser.Item2, keyAndUser.Item1);
                    this.Client.Bind(s => s.IsConnected, (o, e) =>
                    {
                        this.NotifyOfPropertyChange(() => CanMountVolume);
                        this.NotifyOfPropertyChange(() => CanCreateVolume);
                    });

                    await this.Client.ConnectAsync(this.Logger);

                    await Task.WhenAll((await this.Instance.ListVolumesAsync()).Select(volume =>
                        {
                            var volumeViewModel = this.volumeViewModelFactory.CreatetVolumeViewModel();
                            this.ActivateItem(volumeViewModel);
                            return volumeViewModel.ReconnectAsync(volume, this.Client);
                        }));
                }, this.CancelCts.Token);

            try
            {
                await Task.WhenAll(reconnectTask, this.RefreshVolumes());
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
            get { return this.InstanceState == "running" && this.Instance != null && this.Instance.InstanceState == "running"; }
        }

        public async void Terminate()
        {
            this.ActivateItem(this.Items[0]);
            this.uptimeTimer.Stop();

            this.InstanceState = "terminating";

            this.Logger.Log("Umounting all volumes");
            await Task.WhenAll(this.Items.Where(x => x is VolumeViewModel).Select(x => ((VolumeViewModel)x).UnmountVolumeAsync()));

            await this.Instance.DestroyAsync();
            this.TryClose();
        }

        public bool CanMountVolume
        {
            get
            {
                return this.InstanceState == "running" && this.Instance != null &&
                    this.Instance.InstanceState == "running" && this.Client != null &&
                    this.SelectedVolumeType != null &&
                    (this.SelectedVolumeType.IsCustom == true || this.SelectedVolumeType.SnapshotId != null) &&
                    (!this.selectedVolumeType.IsCustom || !string.IsNullOrWhiteSpace(this.CustomVolumeSnapshotId)) &&
                    this.Client.IsConnected;
            }
        }

        public async void MountVolume()
        {
            var volumeViewModel = this.volumeViewModelFactory.CreatetVolumeViewModel();
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
                var volume = this.Instance.CreateVolume(volumeId, volumeName);
                await volumeViewModel.SetupAsync(volume, this.Client);
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

        public bool CanCreateVolume
        {
            get
            {
                return this.InstanceState == "running" && this.Instance != null &&
                    this.Instance.InstanceState == "running" && this.Client != null && this.Client.IsConnected; }
        }

        public async void CreateVolume()
        {
            var result = this.windowManager.ShowDialog<CreateNewVolumeDetailsViewModel>(settings: new Dictionary<string, object>()
            {
                { "ResizeMode", ResizeMode.NoResize },
            });

            if (result.Success)
            {
                var volumeViewModel = this.volumeViewModelFactory.CreatetVolumeViewModel();

                this.ActivateItem(volumeViewModel);

                try
                {
                    var volume = this.Instance.CreateVolume(result.VM.Size, result.VM.Name);
                    await volumeViewModel.SetupAsync(volume, this.Client);
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
        }

        public bool CanLaunchPutty
        {
            get { return this.Instance != null && this.Instance.InstanceState == "running"; }
        }
        public void LaunchPutty()
        {
            var puttyPath = this.config.MainConfig.PuttyPath;
            if (string.IsNullOrEmpty(puttyPath) || !File.Exists(puttyPath))
            {
                var dialog = new OpenFileDialog()
                {
                    Title = "Browse to your PuTTY executable",
                    CheckFileExists = true,
                    Filter = "Executables (*.exe)|*.exe",
                };
                var result = dialog.ShowDialog();

                if (!result.HasValue || !result.Value)
                    return;

                puttyPath = dialog.FileName;
                this.config.MainConfig.PuttyPath = puttyPath;
                this.config.SaveMainConfig();
            }

            var puttyKey = this.CreatePuttyKey();
            var tempFile = Path.GetTempFileName();
            File.WriteAllText(tempFile, puttyKey);

            Process.Start(puttyPath, "-i " + tempFile + " -ssh " + this.Client.User + "@" + this.Client.Host);
        }

        public bool CanSavePrivateKey
        {
            get { return this.Instance != null && this.Instance.InstanceState == "running"; }
        }
        public void SavePrivateKey()
        {
            var dialog = new SaveFileDialog()
            {
                FileName = "id_rsa",
                Filter = "All Files|*.*",
            };
            var result = dialog.ShowDialog();
            if (result == true)
            {
                string fileName = dialog.FileName;
                File.WriteAllText(fileName, this.Instance.PrivateKey);
            }
        }

        public bool CanSavePuttyKey
        {
            get { return this.Instance != null && this.Instance.InstanceState == "running"; }
        }
        public void SavePuttyKey()
        {
            var puttyKey = this.CreatePuttyKey();
            var dialog = new SaveFileDialog()
            {
                FileName = "key.ppk",
                DefaultExt = "ppk",
                AddExtension = true,
                Filter = "PuTTY Key (*.ppk)|*.ppk",
            };
            var result = dialog.ShowDialog();
            if (result == true)
            {
                string fileName = dialog.FileName;
                File.WriteAllText(fileName, puttyKey);
            }
        }
    }

    public interface IVolumeViewModelFactory
    {
        VolumeViewModel CreatetVolumeViewModel();
    }
}
