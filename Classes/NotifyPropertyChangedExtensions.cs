using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Threading.Tasks;

namespace Ec2Manager.Classes
{
    public static class NotifyPropertyChangedExtensions
    {
        public static PropertyChangedEventHandler Bind<T, TR>(this T cls, Expression<Func<T, TR>> property, PropertyChangedEventHandler handler) where T : INotifyPropertyChanged
        {
            var body = property.Body as MemberExpression;

            if (body == null)
                throw new ArgumentException("Not MemberExpression", "property");

            PropertyChangedEventHandler ourHandler = (o, e) =>
            {
                if (e.PropertyName == body.Member.Name)
                    handler(o, e);
            };

            cls.PropertyChanged += ourHandler;

            return ourHandler;
        }

        public static void Unbind(this INotifyPropertyChanged cls, PropertyChangedEventHandler handler)
        {
            cls.PropertyChanged -= handler;
        }
    }
}
