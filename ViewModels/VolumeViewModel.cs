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

        private InstanceClient client;
        public InstanceClient Client
        {
            get { return this.client; }
            set
            {
                this.client = value;
                this.NotifyOfPropertyChange();
            }
        }

        private string mountPointDir;
        public string MountPointDir
        {
            get { return this.mountPointDir; }
            set
            {
                this.mountPointDir = value;
                this.NotifyOfPropertyChange();
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

        [ImportingConstructor]
        public VolumeViewModel(Logger logger)
        {
            this.Logger = logger;
        }

        protected override void OnInitialize()
        {
            base.OnInitialize();

            this.RunCommand = this.Client.GetRunCommand(this.MountPointDir, this.Logger);
        }

        public async void StartGame()
        {
            await this.Client.RunCommandAsync(this.MountPointDir, this.RunCommand, this.Logger);
        }
    }
}
