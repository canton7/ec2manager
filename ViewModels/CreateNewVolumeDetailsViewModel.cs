using Caliburn.Micro;
using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Ec2Manager.ViewModels
{
    [Export]
    public class CreateNewVolumeDetailsViewModel : Screen
    {
        private string name = "New Volume";
        public string Name
        {
            get { return this.name; }
            set
            {
                this.name = value;
                this.NotifyOfPropertyChange();
                this.NotifyOfPropertyChange(() => CanContinue);
            }
        }

        private int size = 1;
        public int Size
        {
            get { return this.size; }
            set
            {
                this.size = value;
                this.NotifyOfPropertyChange();
                this.NotifyOfPropertyChange(() => CanContinue);
            }
        }

        [ImportingConstructor]
        public CreateNewVolumeDetailsViewModel()
        {
            this.DisplayName = "New Volume Details";
        }

        public bool CanContinue
        {
            get { return !string.IsNullOrWhiteSpace(this.Name) && this.Size > 0; }
        }
        public void Continue()
        {
            this.TryClose(true);
        }
    }
}
