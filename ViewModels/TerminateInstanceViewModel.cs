﻿using Caliburn.Micro;
using Ec2Manager.Ec2Manager;
using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Ec2Manager.ViewModels
{
    [Export]
    [PartCreationPolicy(CreationPolicy.NonShared)]
    public class TerminateInstanceViewModel : Screen
    {
        private Logger logger;
        public Logger Logger
        {
            get { return this.logger; }
            set
            {
                this.logger = value;
                this.NotifyOfPropertyChange();
            }
        }

        private Ec2Instance instance;
        public Ec2Instance Instance
        {
            get { return this.instance; }
            private set
            {
                this.instance = value;
                this.NotifyOfPropertyChange();
            }
        }

        [ImportingConstructor]
        public TerminateInstanceViewModel(Logger logger)
        {
            this.Logger = logger;
            this.DisplayName = "Terminating Instance";
        }

        public async Task SetupAsync(Ec2Instance instance)
        {
            this.Instance = instance;
            this.Instance.Logger = this.logger;
            this.DisplayName = "Terminating " + this.Instance.Name;
            await this.Instance.DestroyAsync();
            this.TryClose();
        }
    }
}
