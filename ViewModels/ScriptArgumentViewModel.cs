using Stylet;
using Ec2Manager.Classes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Ec2Manager.ViewModels
{
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

        private ScriptArgumentType type;
        public ScriptArgumentType Type
        {
            get { return this.type; }
            set
            {
                this.type = value;
                this.NotifyOfPropertyChange();
                this.NotifyOfPropertyChange(() => IsString);
                this.NotifyOfPropertyChange(() => IsBool);
            }
        }

        private string[] typeParams;
        public string[] TypeParams
        {
            get { return this.typeParams; }
            set
            {
                this.typeParams = value;
                this.NotifyOfPropertyChange();
            }
        }

        private string value;
        public string Value
        {
            get { return this.value; }
            set
            {
                this.value = value;
                this.NotifyOfPropertyChange();
            }
        }

        public bool IsString
        {
            get { return this.Type == ScriptArgumentType.String; }
        }

        public bool IsBool
        {
            get { return this.Type == ScriptArgumentType.Bool; }
        }

        public ScriptArgumentViewModel()
        {
        }
    }
}
