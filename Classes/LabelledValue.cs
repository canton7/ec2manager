using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Ec2Manager.Classes
{
    class LabelledValue
    {
        public string Label { get; set; }
        public string Value { get; set; }

        public LabelledValue()
        {
        }

        public LabelledValue(string label, string value)
        {
            this.Label = label;
            this.Value = value;
        }
    }
}
