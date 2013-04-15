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
    class ShellViewModel : Conductor<IScreen>.Collection.OneActive
    {
        [ImportingConstructor]
        public ShellViewModel(ConnectViewModel connectModel)
        {
            this.ActivateItem(connectModel);
        }
    }
}
