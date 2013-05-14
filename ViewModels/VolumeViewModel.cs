using Caliburn.Micro;
using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Ec2Manager.Classes;
using System.Threading;

namespace Ec2Manager.ViewModels
{
    [Export]
    [PartCreationPolicy(CreationPolicy.NonShared)]
    public class VolumeViewModel : Screen
    {
        public Logger Logger { get; private set; }

        public InstanceClient Client { get; private set; }
        public Ec2Manager Manager { get; private set; }
        public string MountPointDir { get; private set; }
        public string VolumeId { get; private set; }
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

        private string runCommand;
        public string RunCommand
        {
            get { return this.runCommand; }
            set
            {
                this.runCommand = value;
                this.NotifyOfPropertyChange();
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
            }
        }

        [ImportingConstructor]
        public VolumeViewModel(Logger logger)
        {
            this.Logger = logger;
        }

        public async Task SetupAsync(Ec2Manager manager, InstanceClient client, string volumeName, string volumeId)
        {
            this.Client = client;
            this.Manager = manager;
            this.VolumeId = volumeId;

            this.DisplayName = volumeName;

            try
            {
                this.cancelCts = new CancellationTokenSource();
                this.MountPointDir = await this.Manager.MountVolumeAsync(volumeId, this.Client, volumeName, this.cancelCts.Token, this.Logger);
                this.VolumeState = "mounted";
                this.RunCommand = this.Client.GetRunCommand(this.MountPointDir, this.Logger);
                this.UserInstruction = this.Client.GetUserInstruction(this.MountPointDir, this.Logger).Replace("<PUBLIC-IP>", this.Manager.PublicIp);
            }
            finally
            {
                this.cancelCts = null;
            }
        }

        public void Reconnect(Ec2Manager manager, InstanceClient client, string volumeName, string volumeId, string mountPointDir)
        {
            this.Client = client;
            this.Manager = manager;
            this.VolumeId = volumeId;
            this.MountPointDir = mountPointDir;

            this.DisplayName = volumeName;

            this.RunCommand = this.Client.GetRunCommand(this.MountPointDir, this.Logger);
            this.UserInstruction = this.Client.GetUserInstruction(this.MountPointDir, this.Logger).Replace("<PUBLIC-IP>", this.Manager.PublicIp);
            this.Logger.Log("Reconnected to volume");

            if (this.Client.IsCommandSessionStarted(this.MountPointDir))
            {
                this.VolumeState = "started";
                this.gameCts = new CancellationTokenSource();
                var resumeTask = this.Client.ResumeSessionAsync(this.MountPointDir, this.Logger, this.gameCts.Token);
            }
            else
            {
                this.VolumeState = "mounted";
            }
        }

        public bool CanStartGame
        {
            get
            {
                return this.VolumeState == "mounted";
            }
        }
        public void StartGame()
        {
            this.Logger.Log("Starting to launch game...");
            this.VolumeState = "started";
            this.gameCts = new CancellationTokenSource();
            var runTask = this.Client.RunCommandAsync(this.MountPointDir, this.RunCommand, this.MountPointDir, this.Logger, this.gameCts.Token);
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
                return this.VolumeState == "mounted" || this.VolumeState == "started";
            }
        }
        public async void UnmountVolume()
        {
            this.Logger.Log("Starting volume unmount sequence");

            if (this.VolumeState == "started")
                this.StopGame();

            this.VolumeState = "unmounting";
            this.Logger.Log("Deleting volume");
            await this.Manager.DeleteVolumeAsync(this.VolumeId, this.Logger);
            this.VolumeState = "unmounted";
            this.Logger.Log("Done");
            this.TryClose();
        }
    }
}
