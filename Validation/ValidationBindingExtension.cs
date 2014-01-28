using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;
using System.Windows.Markup;

namespace Ec2Manager.Validation
{
    public class ValidationBindingExtension : MarkupExtension
    {
        [ConstructorArgument("Path")]
        public PropertyPath Path { get; set; }

        public bool ReplaceInvalidValues { get; set; }
        public object ReplaceInvalidValuesWith { get; set; }

        public IValueConverter Converter { get; set; }
        public string StringFormat { get; set; }
        public UpdateSourceTrigger UpdateSourceTrigger { get; set; }
        public object TargetNullValue { get; set; }

        public ValidationBindingExtension(string path)
        {
            this.Path = new PropertyPath(path);

            this.ReplaceInvalidValues = true;
            this.ReplaceInvalidValuesWith = null;

            this.UpdateSourceTrigger = UpdateSourceTrigger.LostFocus;
        }

        public override object ProvideValue(IServiceProvider serviceProvider)
        {
            var binding = new Binding(this.Path.Path);

            binding.ValidatesOnDataErrors = true;
            binding.ValidatesOnExceptions = true;

            binding.UpdateSourceTrigger = this.UpdateSourceTrigger;
            binding.StringFormat = this.StringFormat;
            binding.TargetNullValue = this.TargetNullValue;

            if (this.ReplaceInvalidValues)
            {
                var converter = new ReplaceInvalidValuesConverter();
                converter.ReplaceInvalidValuesWith = this.ReplaceInvalidValues;

                if (this.Converter == null)
                    binding.Converter = converter;
                else
                    binding.Converter = new ValueConverterGroup() { this.Converter, converter };
            }
            else
            {
                binding.Converter = this.Converter;
            }

            return binding.ProvideValue(serviceProvider);
        }
    }

    public class ReplaceInvalidValuesConverter : DependencyObject, IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            return value;
        }

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            var converter = TypeDescriptor.GetConverter(targetType);
            if (!converter.IsValid(value))
                return null;

            var convertedValue = converter.ConvertFrom(value);
            return convertedValue;
        }

        public object ReplaceInvalidValuesWith
        {
            get { return (object)GetValue(ReplaceInvalidValuesWithProperty); }
            set { SetValue(ReplaceInvalidValuesWithProperty, value); }
        }

        public static readonly DependencyProperty ReplaceInvalidValuesWithProperty =
        DependencyProperty.Register("ReplaceInvalidValuesWith", typeof(object), typeof(ReplaceInvalidValuesConverter), new PropertyMetadata(null));
    }

    // http://stackoverflow.com/a/8326207/1086121
    public class ValueConverterGroup : List<IValueConverter>, IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            return this.Aggregate(value, (current, converter) => converter.Convert(current, targetType, parameter, culture));
        }

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            return this.Aggregate(value, (current, converter) => converter.ConvertBack(current, targetType, parameter, culture));
        }
    }
}
