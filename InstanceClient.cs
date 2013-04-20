using Renci.SshNet;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Ec2Manager
{
    public class InstanceClient : IDisposable, IMachineInteractionProvider
    {
        private SshClient client;

        private string user;

        public InstanceClient(string host, string user, string key)
        {
            this.user = user;

            this.client = new SshClient(host, user, new PrivateKeyFile(new MemoryStream(Encoding.ASCII.GetBytes(key))));
            while (!this.client.IsConnected)
            {
                try
                {
                    this.client.Connect();
                }
                catch (System.Net.Sockets.SocketException)
                {
                }
            }
        }

        public void Setup()
        {
        //    SshCommand cmd;

        //    this.SetupS3Cmd();
        //    cmd = this.client.RunCommand("cat ~/.s3cmd");
        //    Debug.Print(cmd.Result);

        //    cmd = this.client.RunCommand("s3cmd ls");
        //    Debug.Print(cmd.Result);

        //    cmd = this.client.RunCommand("sudo chown -R ubuntu.ubuntu /mnt");
        }

        //private void SetupS3Cmd()
        //{
        //    this.client.RunCommand("sudo apt-get install s3cmd");
        //    this.client.RunCommand("echo [default] > ~/.s3cfg");
        //    this.client.RunCommand("echo access_key = " + this.accessKey + " >> ~/.s3cfg");
        //    this.client.RunCommand("echo secret_key = " + this.secretKey + " >> ~/.s3cfg");
        //    this.client.RunCommand("echo bucket_location = EU >> ~/.s3cfg");
        //}

        public void MountAndSetupDevice(string device, string mountPoint, Logger logger)
        {
            this.RunAndLog("sudo mkdir \"" + mountPoint + "\"", logger);
            this.RunAndLog("sudo mount " + device + " \"" + mountPoint + "\"", logger);
            this.RunAndLog("sudo chown -R " + user + "." + user + " \"" + mountPoint + "\"", logger);

            this.RunAndLog("[ -x \"" + mountPoint + "/ec2manager/setup\" ] && \"" + mountPoint + "/ec2manager/setup\"", logger);
        }

        private void RunAndLog(string command, Logger logger)
        {
            logger.Log(command);
            var cmd = this.client.RunCommand(command);
            logger.Log(cmd.Result);
        }

        public void Dispose()
        {
            if (this.client != null)
                this.client.Disconnect();
        }
    }
}
