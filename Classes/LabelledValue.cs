using Caliburn.Micro;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Ec2Manager.Classes
{
    public class LabelledValue : LabelledValue<string>
    {
        public LabelledValue(string label, string value) : base(label, value)
        {
        }
    }

    public class LabelledValue<T> : PropertyChangedBase
    {
        private string label;
        public string Label
        {
            get { return this.label; }
            set
            {
                this.label = value;
                this.NotifyOfPropertyChange();
            }
        }

        private T value;
        public T Value
        {
            get { return this.value; }
            set
            {
                this.value = value;
                this.NotifyOfPropertyChange();
                this.NotifyOfPropertyChange(() => IsSet);
            }
        }

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
