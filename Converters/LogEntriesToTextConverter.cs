using Stylet;
using Ec2Manager.Utilities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Data;

namespace Ec2Manager.Converters
{
    public class LogEntriesToTextConverter : IValueConverter
    {
        public static readonly LogEntriesToTextConverter Instance = new LogEntriesToTextConverter();

        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            var entries = value as BindableCollection<LogEntry>;
            if (entries == null)
                return null;

            var entriesArray = entries.ToArray();

            return string.Join("\n", entriesArray.Select(x => String.Format("[{0:t}] {1}", x.Time, x.Message)));
        }

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
