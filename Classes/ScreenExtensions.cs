using Caliburn.Micro;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;

namespace Ec2Manager.Classes
{
    public static class ScreenExtensions
    {
        public static void Invoke(this IScreen cls, System.Action action)
        {
            if (Application.Current.Dispatcher.CheckAccess())
                action();
            else
                Application.Current.Dispatcher.Invoke(action);
        }
    }
}
