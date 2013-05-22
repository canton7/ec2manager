using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Ec2Manager.Classes
{
    public class LabelledValue
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

    public class LabelledValue<T>
    {
        public string Label { get; set; }
        public T Value { get; set; }

        public bool IsSet
        {
            get { return this.Value != null; }
        }

        public LabelledValue()
        {
        }

        public LabelledValue(string label, T value)
        {
            this.Label = label;
            this.Value = value;
        }
    }
}
