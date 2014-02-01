using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;

namespace Ec2Manager.Converters
{
    public class BoolToVisibilityConverter : DependencyObject, IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            if (!(value is bool))
                return null;

            bool boolVal = (bool)value;
            return boolVal ? this.TrueVisibility : this.FalseVisibility;
        }

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            throw new NotImplementedException();
        }



        public Visibility FalseVisibility
        {
            get { return (Visibility)GetValue(FalseVisibilityProperty); }
            set { SetValue(FalseVisibilityProperty, value); }
        }

        // Using a DependencyProperty as the backing store for FalseVisibility.  This enables animation, styling, binding, etc...
        public static readonly DependencyProperty FalseVisibilityProperty =
            DependencyProperty.Register("FalseVisibility", typeof(Visibility), typeof(BoolToVisibilityConverter), new PropertyMetadata(Visibility.Hidden));



        public Visibility TrueVisibility
        {
            get { return (Visibility)GetValue(TrueVisibilityProperty); }
            set { SetValue(TrueVisibilityProperty, value); }
        }

        // Using a DependencyProperty as the backing store for TrueVisibility.  This enables animation, styling, binding, etc...
        public static readonly DependencyProperty TrueVisibilityProperty =
            DependencyProperty.Register("TrueVisibility", typeof(Visibility), typeof(BoolToVisibilityConverter), new PropertyMetadata(Visibility.Visible));

        
    }
}
