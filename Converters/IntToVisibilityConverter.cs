using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;

namespace Ec2Manager.Converters
{
    public class IntToVisibilityConverter : DependencyObject, IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            if (!(value is int) || value == null)
                return null;

            return (int)value > this.Threshold ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            throw new NotImplementedException();
        }

        public int Threshold
        {
            get { return (int)GetValue(ThresholdProperty); }
            set { SetValue(ThresholdProperty, value); }
        }

        // Using a DependencyProperty as the backing store for Threshold.  This enables animation, styling, binding, etc...
        public static readonly DependencyProperty ThresholdProperty =
            DependencyProperty.Register("Threshold", typeof(int), typeof(IntToVisibilityConverter), new PropertyMetadata(0));

        
    }
}
