﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Ec2Manager.ViewModels;
using System.Diagnostics;
using System.Windows;
using Stylet;
using Ec2Manager.Model;
using Ec2Manager.Ec2Manager;
using Ec2Manager.Utilities;
using Ec2Manager.Configuration;

namespace Ec2Manager.Core
{
    class AppBootstrapper : Bootstrapper<ShellViewModel>
    {
        protected override void ConfigureIoC(StyletIoC.IStyletIoCBuilder builder)
        {
            builder.Bind<IWindowManager>().To<WindowManager>().InSingletonScope();
            builder.Bind<IEventAggregator>().To<EventAggregator>().InSingletonScope();

            builder.Bind<MainModel>().ToSelf().InSingletonScope();
            builder.Bind<Ec2Connection>().ToSelf().InSingletonScope();
            builder.Bind<VersionManager>().ToSelf().InSingletonScope();
            builder.Bind<Config>().ToSelf().InSingletonScope();

            builder.Bind<IInstanceViewModelFactory>().ToAbstractFactory();
            builder.Bind<IVolumeViewModelFactory>().ToAbstractFactory();
            builder.Bind<IScriptArgumentViewModelFactory>().ToAbstractFactory();
            builder.Bind<IScriptDetailsViewModelFactory>().ToAbstractFactory();
            builder.Bind<ICreateSnapshotDetailsViewModelFactory>().ToAbstractFactory();
            builder.Bind<ITerminateInstanceViewModelFactory>().ToAbstractFactory();
        }

        protected override void OnUnhandledExecption(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
        {
            base.OnUnhandledExecption(sender, e);

            string message = e.Exception.InnerException == null ? e.Exception.Message : e.Exception.InnerException.Message;
            MessageBox.Show(Application.Current.MainWindow, "Error occurred: " + message, "Error occurred", MessageBoxButton.OK, MessageBoxImage.Error);
            e.Handled = true;
        }
    }
}