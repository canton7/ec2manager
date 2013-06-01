using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Caliburn.Micro;
using System.ComponentModel.Composition;
using System.ComponentModel.Composition.Hosting;
using System.ComponentModel.Composition.Primitives;
using Ec2Manager.ViewModels;
using System.Diagnostics;
using System.Windows;

namespace Ec2Manager
{
    class AppBootstrapper : Bootstrapper<ShellViewModel>
    {
        private CompositionContainer container;

        protected override void Configure()
        {
            this.container = new CompositionContainer(new AggregateCatalog(AssemblySource.Instance.Select(x => new AssemblyCatalog(x)).OfType<ComposablePartCatalog>()));

            var batch = new CompositionBatch();

            batch.AddExportedValue<IWindowManager>(new WindowManager());
            batch.AddExportedValue<IEventAggregator>(new EventAggregator());
            batch.AddExportedValue(container);

            container.Compose(batch);

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
            string contract = string.IsNullOrEmpty(key) ? AttributedModelServices.GetContractName(service) : key;
            var exports = this.container.GetExportedValues<object>(contract);

            if (exports.Count() > 0)
                return exports.First();

            throw new Exception(string.Format("Could not locate any instances of contract {0}.", contract));
        }

        protected override void OnUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
        {
            base.OnUnhandledException(sender, e);

            string message = e.Exception.InnerException == null ? e.Exception.Message : e.Exception.InnerException.Message;
            MessageBox.Show(Application.Current.MainWindow, "Error occurred: " + message, "Error occurred", MessageBoxButton.OK, MessageBoxImage.Error);
            e.Handled = true;
        }

        protected override IEnumerable<object> GetAllInstances(Type service)
        {
            return this.container.GetExportedValues<object>(AttributedModelServices.GetContractName(service));
        }

        protected override void BuildUp(object instance)
        {
            this.container.SatisfyImportsOnce(instance);
        }
    }
}