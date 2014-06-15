using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Ec2Manager.Classes;
using System.Threading;
using System.Windows;
using Ec2Manager.Ec2Manager;
using Ec2Manager.Utilities;
using Stylet;

namespace Ec2Manager.ViewModels
{
    public class VolumeViewModel : Screen
    {
        private Ec2Connection connection;
        private ICreateSnapshotDetailsViewModelFactory createSnapshotDetailsViewModelFactory;
        private IScriptDetailsViewModelFactory scriptDetailsViewModelFactory;

        public Logger Logger { get; private set; }
        private IWindowManager windowManager;

        public InstanceClient Client { get; private set; }

        private Ec2Volume volume;
        public Ec2Volume Volume
        {
            get { return this.volume; }
            private set
            {
                this.volume = value;
                this.NotifyOfPropertyChange();
            }
        }

        private CancellationTokenSource gameCts;
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

        private LabelledValue<string>[] runCommands = new LabelledValue<string>[0];
        public LabelledValue<string>[] RunCommands
        {
            get { return this.runCommands; }
            set
            {
                this.runCommands = value;
                this.NotifyOfPropertyChange();

                if (this.runCommands.Length < 1)
                    this.SelectedRunCommand = new LabelledValue<string>("No Commands", null);
                else
                    this.SelectedRunCommand = this.runCommands[0];
            }
        }

        private LabelledValue<string> selectedRunCommand = new LabelledValue<string>("Loading...", null);
        public LabelledValue<string> SelectedRunCommand
        {
            get { return this.selectedRunCommand; }
            set
            {
                this.selectedRunCommand = value;
                this.NotifyOfPropertyChange();
                this.NotifyOfPropertyChange(() => CanStartGame);
            }
        }

        private string userInstruction = "No instruction";
        public string UserInstruction
        {
            get { return this.userInstruction; }
            private set
            {
                this.userInstruction = value;
                this.NotifyOfPropertyChange();
            }
        }

        private string volumeState = "unmounted";
        public string VolumeState
        {
            get { return this.volumeState; }
            set
            {
                this.volumeState = value;
                this.NotifyOfPropertyChange();
                this.NotifyOfPropertyChange(() => CanStartGame);
                this.NotifyOfPropertyChange(() => CanStopGame);
                this.NotifyOfPropertyChange(() => CanUnmountVolume);
                this.NotifyOfPropertyChange(() => CanCreateSnapshot);
                this.NotifyOfPropertyChange(() => CanStartScript);
            }
        }

        private LabelledValue<bool>[] scripts = new[] { new LabelledValue<bool>("Loading...", false) };
        public LabelledValue<bool>[] Scripts
        {
            get { return this.scripts; }
            set
            {
                this.scripts = value;
                if (this.scripts.Length == 0)
                    this.scripts = new[] { new LabelledValue<bool>("No Scripts", false) };
                this.SelectedScript = this.scripts[0];
                this.NotifyOfPropertyChange();
            }
        }

        private LabelledValue<bool> selectedScript;
        public LabelledValue<bool> SelectedScript
        {
            get { return this.selectedScript; }
            set
            {
                this.selectedScript = value;
                this.NotifyOfPropertyChange();
                this.NotifyOfPropertyChange(() => CanStartScript);
            }
        }


        public VolumeViewModel(
            Logger logger,
            IWindowManager windowManager,
            Ec2Connection connection,
            ICreateSnapshotDetailsViewModelFactory createSnapshotDetailsViewModelFactory,
            IScriptDetailsViewModelFactory scriptDetailsViewModelFactory)
        {
            this.Logger = logger;
            this.windowManager = windowManager;
            this.connection = connection;
            this.createSnapshotDetailsViewModelFactory = createSnapshotDetailsViewModelFactory;
            this.scriptDetailsViewModelFactory = scriptDetailsViewModelFactory;

            this.SelectedScript = this.Scripts[0];
        }

        public async Task SetupAsync(Ec2Volume volume, InstanceClient client)
        {
            this.Client = client;
            this.Volume = volume;

            this.Volume.Logger = this.Logger;

            this.DisplayName = volume.Name;

            try
            {
                this.cancelCts = new CancellationTokenSource();
                await this.Volume.SetupAsync(this.Client, this.cancelCts.Token);
                this.VolumeState = "mounted";
                this.RunCommands = (await this.Client.GetRunCommandsAsync(this.Volume.MountPoint, this.cancelCts.Token)).ToArray();
                this.UserInstruction = (await this.Client.GetUserInstructionAsync(this.Volume.MountPoint, this.cancelCts.Token)).Replace("<PUBLIC-IP>", this.Volume.Instance.PublicIp);
                await this.UpdateScriptsAsync();
            }
            finally
            {
                this.CancelCts = null;
            }
        }

        public async Task ReconnectAsync(Ec2Volume volume, InstanceClient client, CancellationToken? cancellationToken = null)
        {
            this.Client = client;
            this.Volume = volume;

            this.DisplayName = volume.Name;
            this.Volume.Logger = this.Logger;

            this.RunCommands = (await this.Client.GetRunCommandsAsync(this.Volume.MountPoint)).ToArray();
            this.UserInstruction = (await this.Client.GetUserInstructionAsync(this.Volume.MountPoint)).Replace("<PUBLIC-IP>", this.Volume.Instance.PublicIp);
            this.Logger.Log("Reconnected to volume");
            await this.UpdateScriptsAsync();

            if (await this.Client.IsCommandSessionStartedAsync(this.Volume.MountPoint, cancellationToken))
            {
                this.VolumeState = "started";
                this.gameCts = new CancellationTokenSource();
                await this.Client.ResumeSessionAsync(this.Volume.MountPoint, this.Logger, this.gameCts.Token);                
            }
            else
            {
                this.VolumeState = "mounted";
            }
        }

        private async Task UpdateScriptsAsync()
        {
            this.Scripts = (await this.Client.ListScriptsAsync(this.Volume.MountPoint)).Select(x => new LabelledValue<bool>(x, true)).ToArray();
        }

        public bool CanStartGame
        {
            get
            {
                return this.VolumeState == "mounted" && !string.IsNullOrWhiteSpace(this.SelectedRunCommand.Value);
            }
        }
        public void StartGame()
        {
            this.Logger.Log("Starting to launch game...");
            this.VolumeState = "started";
            this.gameCts = new CancellationTokenSource();
            var runTask = this.Client.RunCommandAsync(this.Volume.MountPoint, this.SelectedRunCommand.Value, this.Volume.MountPoint, this.Logger, this.gameCts.Token);
        }

        public bool CanStopGame
        {
            get
            {
                return this.VolumeState == "started";
            }
        }
        public void StopGame()
        {
            this.Logger.Log("Stopping game...");
            this.gameCts.Cancel();
            this.VolumeState = "mounted";
            this.Logger.Log("Game stopped");
        }

        public bool CanCancelAction
        {
            get { return this.cancelCts != null && !this.cancelCts.IsCancellationRequested; }
        }
        public void CancelAction()
        {
            if (this.CancelCts == null)
                return;

            this.Logger.Log("Starting to cancel operation");
            this.Logger.Log("This can take a while...");
            this.CancelCts.Cancel();
            this.NotifyOfPropertyChange(() => CanCancelAction);
        }

        public bool CanUnmountVolume
        {
            get
            {
                return this.VolumeState == "mounted";
            }
        }
        public async void UnmountVolume()
        {
            await this.UnmountVolumeAsync();
        }

        public async Task UnmountVolumeAsync()
        {
            this.Logger.Log("Starting volume unmount sequence");

            if (this.VolumeState == "started")
                this.StopGame();

            this.VolumeState = "unmounting";
            this.Logger.Log("Deleting volume");
            await this.Volume.DeleteAsync();
            this.VolumeState = "unmounted";
            this.Logger.Log("Done");
            this.TryClose();
        }

        public bool CanCreateSnapshot
        {
            get { return this.VolumeState == "mounted"; }
        }
        public async void CreateSnapshot()
        {
            var detailsModel = this.createSnapshotDetailsViewModelFactory.CreateCreateSnapshotDetailsViewModel();

            var description = await this.Volume.GetSourceSnapshotDescriptionAsync();
            var hasSourceSnapshot = description != null;
            var userOwnsSourceSnapshot = hasSourceSnapshot && description.OwnerId == await this.connection.GetUserIdAsync();
            if (!hasSourceSnapshot)
            {
                detailsModel.HasSourceSnapshotToDelete = false;
            }
            else
            {
                // Only allow them to delete if they actually own it
                detailsModel.HasSourceSnapshotToDelete = userOwnsSourceSnapshot;
                detailsModel.Name = description.Name;
                detailsModel.Description = description.Description;
            }

            var result = this.windowManager.ShowDialog(detailsModel);

            var deleteSourceSnapshot = userOwnsSourceSnapshot && detailsModel.DeleteSourceSnapshot;

            if (result.HasValue && result.Value)
            {
                if (!deleteSourceSnapshot && await this.Volume.AnySnapshotsExistWithName(detailsModel.Name))
                {
                    var confirmResult = MessageBox.Show(Application.Current.MainWindow, "Are you sure you want to create a snapshot called " + detailsModel.Name + "?\nYou already have a snapshot with this name", "Are you sure?", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
                    if (confirmResult == MessageBoxResult.No)
                        return;
                }

                try
                {
                    this.VolumeState = "creating-snapshot";

                    this.CancelCts = new CancellationTokenSource();
                    await this.Volume.CreateSnapshotAsync(detailsModel.Name, detailsModel.Description, detailsModel.IsPublic, this.CancelCts.Token);

                    if (deleteSourceSnapshot)
                        await this.Volume.DeleteSnapshotAsync(await this.Volume.GetSourceSnapshotAsync());
                }
                catch (OperationCanceledException)
                {
                    this.Logger.Log("Snapshot creation cancelled");
                }
                finally
                {
                    this.CancelCts = null;
                    this.VolumeState = "mounted";
                }
            }
        }

        public bool CanStartScript
        {
            get { return this.SelectedScript.Value && this.VolumeState == "mounted"; }
        }
        public async void StartScript()
        {
            var requiredArgs = await this.Client.GetScriptArgumentsAsync(this.Volume.MountPoint, this.SelectedScript.Label);
            var arguments = new string[0];

            if (requiredArgs.Length > 0)
            {
                var vm = this.scriptDetailsViewModelFactory.CreateScriptDetailsViewModel();
                vm.SetArguments(requiredArgs);

                var result = this.windowManager.ShowDialog(vm);
                if (!result.HasValue || !result.Value)
                    return;

                arguments = vm.ScriptArguments.Select(x => x.Value.ToString()).ToArray();
            }

            this.VolumeState = "running-script";
            try
            {
                this.CancelCts = new CancellationTokenSource();
                await this.Client.RunScriptAsync(this.Volume.MountPoint, this.SelectedScript.Label, arguments, this.Logger, this.CancelCts.Token);
            }
            catch (OperationCanceledException)
            {
                this.Logger.Log("Script cancelled");
            }
            finally
            {
                this.CancelCts = null;
                this.VolumeState = "mounted";
            }
        }
    }

    public interface ICreateSnapshotDetailsViewModelFactory
    {
        CreateSnapshotDetailsViewModel CreateCreateSnapshotDetailsViewModel();
    }

    public interface IScriptDetailsViewModelFactory
    {
        ScriptDetailsViewModel CreateScriptDetailsViewModel();
    }
}
