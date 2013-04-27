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

            MessageBox.Show(Application.Current.MainWindow, "Error occurred: " + e.Exception.InnerException.Message, "Error occurred", MessageBoxButton.OK, MessageBoxImage.Error);
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