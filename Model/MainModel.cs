using Ec2Manager.Configuration;
using Ec2Manager.Ec2Manager;
using Ec2Manager.Classes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Stylet;

namespace Ec2Manager.Model
{
    public class MainModel
    {
        public Config Config { get; private set; }
        public Ec2Connection Connection { get; private set; }

        public MainModel(Config config, Ec2Connection connection)
        {
            this.Config = config;
            this.Connection = connection;

            this.Config.MainConfig.Bind(s => s.AwsAccessKey, (o, e) => this.ReconnectConnection());
            this.Config.MainConfig.Bind(s => s.AwsSecretKey, (o, e) => this.ReconnectConnection());

            this.ReconnectConnection();
        }

        private void ReconnectConnection()
        {
            this.Connection.SetCredentials(new Credentials(this.Config.MainConfig.AwsAccessKey, this.Config.MainConfig.AwsSecretKey));
        }
    }
}
