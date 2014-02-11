using Caliburn.Micro;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Ec2Manager.ViewModels
{
    public class CreateSnapshotDetailsViewModel : Screen
    {
        private string name;
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

        private string description;
        public string Description
        {
            get { return this.description; }
            set
            {
                this.description = value;
                this.NotifyOfPropertyChange();
                this.NotifyOfPropertyChange(() => CanContinue);
            }
        }

        private bool isPublic = true;
        public bool IsPublic
        {
            get { return this.isPublic; }
            set
            {
                this.isPublic = value;
                this.NotifyOfPropertyChange();
            }
        }

        private bool hasSourceSnapshot;
        public bool HasSourceSnapshotToDelete
        {
            get { return this.hasSourceSnapshot; }
            set
            {
                this.hasSourceSnapshot = value;
                this.NotifyOfPropertyChange();
            }
        }

        private bool deleteSourceSnapshot = true;
        public bool DeleteSourceSnapshot
        {
            get { return this.deleteSourceSnapshot; }
            set
            {
                this.deleteSourceSnapshot = value;
                this.NotifyOfPropertyChange();
            }
        }

        public CreateSnapshotDetailsViewModel()
        {
            this.DisplayName = "Create Snapshot Settings";
        }


        public bool CanContinue
        {
            get { return !string.IsNullOrWhiteSpace(this.Description) && !string.IsNullOrWhiteSpace(this.Name); }
        }
        public void Continue()
        {
            this.TryClose(true);
        }
    }
}
