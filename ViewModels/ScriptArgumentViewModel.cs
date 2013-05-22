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
    public class ScriptArgumentViewModel : Screen
    {
        private string description;
        public string Description
        {
            get { return this.description; }
            set
            {
                this.description = value;
                this.NotifyOfPropertyChange();
            }
        }

        private Type type;
        public Type Type
        {
            get { return this.type; }
            set
            {
                this.type = value;
                this.NotifyOfPropertyChange();
                this.NotifyOfPropertyChange(() => IsString);
            }
        }

        public object Value { get; set; }

        public bool IsString
        {
            get { return this.Type == typeof(string); }
        }

        [ImportingConstructor]
        public ScriptArgumentViewModel()
        {
        }
    }
}
