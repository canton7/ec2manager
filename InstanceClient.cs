using Ec2Manager.Classes;
using Renci.SshNet;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Ec2Manager
{
    public class InstanceClient : IDisposable, IMachineInteractionProvider
    {
        private SshClient client;
        private string host;
        private string user;
        private string key;
        private AsyncSemaphore setupCmdLock = new AsyncSemaphore(1, 1);

        private string mountBase
        {
            get { return "/home/" + this.user + "/"; }
        }

        public InstanceClient(string host, string user, string key)
        {
            this.host = host;
            this.user = user;
            this.key = key;
        }

        public Task ConnectAsync(Logger logger)
        {
            return Task.Run(() =>
                {
                    logger.Log("Establishing connection with {0}@{1}", user, host);
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
                    logger.Log("Connected");
                });
        }

        public async Task MountAndSetupDeviceAsync(string device, string mountPointDir, Logger logger)
        {
            var mountPoint = this.mountBase + mountPointDir;
            await this.RunAndLogAsync("sudo mkdir \"" + mountPoint + "\"", logger);
            await this.RunAndLogAsync("sudo mount " + device + " \"" + mountPoint + "\"", logger, false, 5);
            await this.RunAndLogAsync("sudo chown -R " + user + "." + user + " \"" + mountPoint + "\"", logger);

            await this.setupCmdLock.WaitAsync();
            {
                await this.RunAndLogStreamAsync("[ -x \"" + mountPoint + "/ec2manager/setup\" ] && \"" + mountPoint + "/ec2manager/setup\"", logger);
            }
            this.setupCmdLock.Release();
        }

        public IEnumerable<PortRangeDescription> GetPortDescriptions(string mountPointDir, Logger logger)
        {
            var mountPoint = this.mountBase + mountPointDir;

            var cmd = this.client.RunCommand("[ -r \"" + mountPoint + "/ec2manager/ports\" ] && cat \"" + mountPoint + "/ec2manager/ports\"");
            return cmd.Result.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None).SelectMany(x =>
                {
                    if (string.IsNullOrWhiteSpace(x))
                        return Enumerable.Empty<PortRangeDescription>(); 

                    var parts = x.Split(new[] { '/' });

                    int fromPort;
                    int toPort;
                    var portParts = parts[0].Split(new[] { '-' });
                    if (portParts.Length == 1)
                    {
                        fromPort = toPort = Convert.ToInt32(portParts[0]);
                    }
                    else
                    {
                        fromPort = Convert.ToInt32(portParts[0]);
                        toPort = Convert.ToInt32(portParts[1]);
                    }

                    if (parts.Length == 1)
                    {
                        return new[]
                        {
                            new PortRangeDescription(fromPort, toPort, "tcp"),
                            new PortRangeDescription(fromPort, toPort, "udp"),
                        };
                    }
                    else
                    {
                        var protoStr = parts[1].ToLowerInvariant();
                        if (protoStr == "tcp" || protoStr == "udp")
                            return new[] { new PortRangeDescription(fromPort, toPort, protoStr) };
                        else
                        {
                            logger.Log("Warning: Cannot interpret protocol on port specification {0}", x);
                            return Enumerable.Empty<PortRangeDescription>();
                        }
                    }
                });
        }

        public string GetRunCommand(string mountPointDir, Logger logger)
        {
            var mountPoint = this.mountBase + mountPointDir;

            return this.client.RunCommand("[ -r \"" + mountPoint + "/ec2manager/runcmd\" ] && cat \"" + mountPoint + "/ec2manager/runcmd\"").Result.Trim();
        }

        public async Task RunCommandAsync(string from, string command, Logger logger, CancellationToken? cancellationToken = null)
        {
            var cmd = "cd \"" + from + "\" && " + command + "";
            await this.RunInShell(cmd, logger, cancellationToken);
        }

        public string GetUserInstruction(string mountPointDir, Logger logger)
        {
            var mountPoint = this.mountBase + mountPointDir;

            return this.client.RunCommand("[ -r \"" + mountPoint + "/ec2manager/user_instruction\" ] && cat \"" + mountPoint + "/ec2manager/user_instruction\"").Result.Trim();
        }

        public string GetUptime()
        {
            return this.client.RunCommand("uptime").Result.Trim();
        }

        private async Task RunAndLogAsync(string command, Logger logger, bool logResult = false, int retryTimes = 0)
        {
            logger.Log(command);
            var cmd = this.client.RunCommand(command);

            for (int i = 0; i < retryTimes + 1; i++)
            {
                if (cmd.ExitStatus == 0)
                {
                    break;
                }
                else
                {
                    logger.Log("Error: {0}", cmd.Error);
                    await Task.Delay(1000);
                }
            }

            if (logResult)
                logger.Log(cmd.Result);
        }

        private Task RunAndLogStreamAsync(string command, Logger logger, bool logCommand = false)
        {
            if (logCommand)
                logger.Log(command);

            return Task.Run(() =>
            {
                var cmd = this.client.CreateCommand(command);
                var result = cmd.BeginExecute();
                logger.LogFromStream(cmd.OutputStream, result);
                cmd.EndExecute(result);
            });
        }

        private Task RunInShell(string command, Logger logger, System.Threading.CancellationToken? cancellationToken = null)
        {
            Action runAction = () =>
                {
                    using (var stream = this.client.CreateShellStream("xterm", 80, 24, 800, 600, 1024))
                    {
                        var reader = new StreamReader(stream);
                        var writer = new StreamWriter(stream);
                        writer.AutoFlush = true;

                        reader.ReadToEnd();
                        writer.WriteLine(command);

                        while (true)
                        {
                            // Command is cancelled by stream being disposed
                            if (cancellationToken.HasValue)
                                cancellationToken.Value.ThrowIfCancellationRequested();

                            var result = reader.ReadToEnd().Trim();
                            if (!string.IsNullOrEmpty(result))
                                logger.Log(result);

                            System.Threading.Thread.Sleep(100);
                        }
                    }
                };

            if (cancellationToken.HasValue)
                return Task.Run(runAction, cancellationToken.Value);
            else
                return Task.Run(runAction);
        }

        public void Dispose()
        {
            if (this.client != null)
                this.client.Disconnect();
        }
    }
}
