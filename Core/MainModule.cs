using Caliburn.Micro;
using Ec2Manager.Configuration;
using Ec2Manager.Ec2Manager;
using Ec2Manager.Model;
using Ec2Manager.Utilities;
using Ninject.Modules;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Ec2Manager.Core
{
    public class MainModule : NinjectModule
    {
        public override void Load()
        {
            this.Bind<IWindowManager>().To<WindowManager>().InSingletonScope();
            this.Bind<IEventAggregator>().To<EventAggregator>().InSingletonScope();

            this.Bind<MainModel>().ToSelf().InSingletonScope();
            this.Bind<Ec2Connection>().ToSelf().InSingletonScope();
            this.Bind<VersionManager>().ToSelf().InSingletonScope();
            this.Bind<Config>().ToSelf().InSingletonScope();
        }
    }
}
