using Caliburn.Micro;
using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Ec2Manager.Classes;
using System.Threading;
using System.Windows;
using Ec2Manager.Ec2Manager;

namespace Ec2Manager.ViewModels
{
    [Export]
    [PartCreationPolicy(CreationPolicy.NonShared)]
    public class VolumeViewModel : Screen
    {
        public Logger Logger { get; private set; }
        private IWindowManager windowManager;

        public InstanceClient Client { get; private set; }
        public Ec2Volume Volume { get; private set; }
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

        private LabelledValue[] runCommands;
        public LabelledValue[] RunCommands
        {
            get { return this.runCommands; }
            set
            {
                this.runCommands = value;
                this.NotifyOfPropertyChange();

                if (this.runCommands.Length < 1)
                    this.SelectedRunCommand = new LabelledValue("No Commands", null);
                else
                    this.SelectedRunCommand = this.runCommands[0];
            }
        }

        private LabelledValue selectedRunCommand = new LabelledValue("Loading...", null);
        public LabelledValue SelectedRunCommand
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

        private LabelledValue<bool>[] scripts;
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

        private LabelledValue<bool> selectedScript = new LabelledValue<bool>("Loading...", false);
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


        [ImportingConstructor]
        public VolumeViewModel(Logger logger, IWindowManager windowManager)
        {
            this.Logger = logger;
            this.windowManager = windowManager;
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
                this.RunCommands = this.Client.GetRunCommands(this.Volume.MountPoint, this.Logger).ToArray();
                this.UserInstruction = this.Client.GetUserInstruction(this.Volume.MountPoint, this.Logger).Replace("<PUBLIC-IP>", this.Volume.Instance.PublicIp);
                this.UpdateScripts();
            }
            finally
            {
                this.cancelCts = null;
            }
        }

        public async Task ReconnectAsync(Ec2Volume volume, InstanceClient client)
        {
            this.Client = client;
            this.Volume = volume;

            this.DisplayName = volume.Name;
            this.Volume.Logger = this.Logger;

            this.RunCommands = this.Client.GetRunCommands(this.Volume.MountPoint, this.Logger).ToArray();
            this.UserInstruction = this.Client.GetUserInstruction(this.Volume.MountPoint, this.Logger).Replace("<PUBLIC-IP>", this.Volume.Instance.PublicIp);
            this.Logger.Log("Reconnected to volume");
            this.UpdateScripts();

            if (this.Client.IsCommandSessionStarted(this.Volume.MountPoint))
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

        private void UpdateScripts()
        {
            this.Scripts = this.Client.ListScripts(this.Volume.MountPoint).Select(x => new LabelledValue<bool>(x, true)).ToArray();
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
            var detailsModel = IoC.Get<CreateSnapshotDetailsViewModel>();
            var result = this.windowManager.ShowDialog(detailsModel, settings: new Dictionary<string, object>()
            {
                { "ResizeMode", ResizeMode.NoResize },
            });

            if (result.HasValue && result.Value)
            {
                try
                {
                    this.VolumeState = "creating-snapshot";

                    this.CancelCts = new CancellationTokenSource();
                    await this.Volume.CreateSnapshotAsync(detailsModel.Name, detailsModel.Description, detailsModel.IsPublic, this.CancelCts.Token);
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
            var requiredArgs = this.Client.GetScriptArguments(this.Volume.MountPoint, this.SelectedScript.Label);
            var arguments = new string[0];

            if (requiredArgs.Length > 0)
            {
                var vm = IoC.Get<ScriptDetailsViewModel>();
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
}
