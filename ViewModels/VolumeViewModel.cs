using Caliburn.Micro;
using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Ec2Manager.Classes;

namespace Ec2Manager.ViewModels
{
    [Export]
    public class VolumeViewModel : Screen
    {
        public Logger Logger { get; private set; }

        public InstanceClient Client { get; set; }
        public Ec2Manager Manager { get; set; }
        public string MountPointDir { get; set; }
        public string VolumeName { get; set; }

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

        private string userInstruction;
        public string UserInstruction
        {
            get { return this.userInstruction; }
            private set
            {
                this.userInstruction = value;
                this.NotifyOfPropertyChange();
            }
        }

        [ImportingConstructor]
        public VolumeViewModel(Logger logger)
        {
            this.Logger = logger;
        }

        protected override void OnInitialize()
        {
            base.OnInitialize();

            this.DisplayName = this.VolumeName;
            this.RunCommand = this.Client.GetRunCommand(this.MountPointDir, this.Logger);
            this.UserInstruction = this.Client.GetUserInstruction(this.MountPointDir, this.Logger).Replace("<PUBLIC-IP>", this.Manager.PublicIp);
        }

        public async void StartGame()
        {
            this.Logger.Log("Starting to launch game...");
            await this.Client.RunCommandAsync(this.MountPointDir, this.RunCommand, this.Logger);
        }
    }
}
