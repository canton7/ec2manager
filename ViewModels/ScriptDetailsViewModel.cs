using Caliburn.Micro;
using Ec2Manager.Classes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Ec2Manager.ViewModels
{
    public class ScriptDetailsViewModel : Screen
    {
        private ScriptArgumentViewModel[] scriptArguments;
        public ScriptArgumentViewModel[] ScriptArguments
        {
            get { return this.scriptArguments; }
            set
            {
                this.scriptArguments = value;
                this.NotifyOfPropertyChange();
            }
        }

        public ScriptDetailsViewModel()
        {
            this.DisplayName = "Script Details";
        }

        public void SetArguments(ScriptArgument[] arguments)
        {
            this.ScriptArguments = arguments.Select(arg =>
                {
                    var vm = IoC.Get<ScriptArgumentViewModel>();

                    vm.Description = arg.Description;
                    vm.Type = arg.Type;
                    vm.TypeParams = arg.TypeParams;
                    vm.Value = arg.DefaultValue;

                    return vm;
                }).ToArray();
        }

        public void Continue()
        {
            this.TryClose(true);
        }
    }
}
