using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Ec2Manager.Classes
{
    public struct ScriptArgument
    {
        public string Description;
        public string DefaultValue;
        public ScriptArgumentType Type;
        public string[] TypeParams;

        public ScriptArgument(string description, string defaultValue, ScriptArgumentType type, string[] typeParams)
        {
            this.Description = description;
            this.DefaultValue = defaultValue;
            this.Type = type;
            this.TypeParams = typeParams;
        }
    }

    public enum ScriptArgumentType
    {
        String,
        Bool,
        Enum,
    }
}
