using Caliburn.Micro;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Ec2Manager.Classes
{
    public struct ShowDialogResult<T>
    {
        public T VM { get; private set; }
        public bool? Result { get; private set; }

        public bool Success
        {
            get { return this.Result.GetValueOrDefault(); }
        }

        public ShowDialogResult(T vm, bool? result) : this()
        {
            this.VM = vm;
            this.Result = result;
        }
    }

    public static class WindowManagerExtensions
    {
        public static ShowDialogResult<T> ShowDialog<T>(this IWindowManager windowManager, object context = null, IDictionary<string, object> settings = null)
        {
            T vm = IoC.Get<T>();
            bool? result = null;

            Caliburn.Micro.Execute.OnUIThread(() =>
                {
                    result = windowManager.ShowDialog(vm, context, settings);
                });

            return new ShowDialogResult<T>(vm, result);
        }
    }
}
