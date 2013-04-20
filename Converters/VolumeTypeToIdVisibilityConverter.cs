using Ec2Manager.Classes;
using Ec2Manager.ViewModels;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;

namespace Ec2Manager.Converters
{
    public class VolumeTypeToIdVisibilityConverter : IValueConverter
    {
        public static readonly VolumeTypeToIdVisibilityConverter Instance = new VolumeTypeToIdVisibilityConverter();

        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            var volumeType = value as VolumeType;
            if (volumeType == null)
                return Visibility.Hidden;

            if (volumeType.IsCustom)
                return Visibility.Visible;
            else
                return Visibility.Hidden;
        }

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
