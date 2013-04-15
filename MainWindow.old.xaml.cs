using Renci.SshNet;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace Ec2Manager
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();

            string accessKey = "xxx";
            string secretKey = "xxx";

            var a = new Ec2Manager(accessKey, secretKey);
            Task.Run(async () =>
                {
                    await a.CreateAsync();

                    using (var client = new InstanceManager(a.PublicIp, "ubuntu", a.PrivateKey, accessKey, secretKey))
                    {
                        client.Setup();

                        await a.MountDevice("snap-03b13e28", "/home/ubuntu/left4dead2", client);
                    }

                    await a.DestroyAsync();
               });
            
        }
    }
}
