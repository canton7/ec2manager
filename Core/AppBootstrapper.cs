using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Caliburn.Micro;
using Ec2Manager.ViewModels;
using System.Diagnostics;
using System.Windows;
using Ninject;

namespace Ec2Manager.Core
{
    class AppBootstrapper : Bootstrapper<ShellViewModel>
    {
        private IKernel kernel;

        protected override void Configure()
        {
            this.kernel = new StandardKernel(new MainModule());

            ActionMessage.ApplyAvailabilityEffect = (context) =>
                {
                    var source = context.Source;
                    if (ConventionManager.HasBinding(source, UIElement.IsEnabledProperty)){
                        return source.IsEnabled;
                    }
                    if (context.CanExecute != null) {
                        source.IsEnabled = context.CanExecute();
                    }
                    else if (context.Target == null) {
                        source.IsEnabled = false;
                    }

                    return source.IsEnabled;
                };
        }

        protected override object GetInstance(Type service, string key)
        {
            return string.IsNullOrEmpty(key) ? this.kernel.Get(service) : this.kernel.Get(service, key);
        }

        protected override IEnumerable<object> GetAllInstances(Type service)
        {
            return this.kernel.GetAll(service);
        }

        protected override void OnUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
        {
            base.OnUnhandledException(sender, e);

            string message = e.Exception.InnerException == null ? e.Exception.Message : e.Exception.InnerException.Message;
            MessageBox.Show(Application.Current.MainWindow, "Error occurred: " + message, "Error occurred", MessageBoxButton.OK, MessageBoxImage.Error);
            e.Handled = true;
        }

        protected override void BuildUp(object instance)
        {
            this.kernel.Inject(instance);
        }

        protected override void OnExit(object sender, EventArgs e)
        {
            this.kernel.Dispose();
        }
    }
}