using Caliburn.Micro;
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
    public class InstanceClient : PropertyChangedBase, IDisposable, IMachineInteractionProvider
    {
        private SshClient client;
        private string host;
        private string user;
        private string key;
        private AsyncSemaphore setupCmdLock = new AsyncSemaphore(1, 1);

        public static readonly Dictionary<string, Type> scriptArgumentTypeMapping = new Dictionary<string, Type>()
        {
            { "string", typeof(string) },
        };

        private bool isConnected = false;
        public bool IsConnected
        {
            get { return this.isConnected; }
            set
            {
                this.isConnected = value;
                this.NotifyOfPropertyChange();
            }
        }

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

        public Task ConnectAsync(ILogger logger)
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
                        catch (System.Net.Sockets.SocketException) { }
                    }
                    this.IsConnected = true;
                    logger.Log("Connected");
                });
        }

        public async Task MountAndSetupDeviceAsync(string device, string mountPointDir, ILogger logger)
        {
            var mountPoint = this.mountBase + mountPointDir;
            await this.RunAndLogAsync("sudo mkdir -p \"" + mountPoint + "\"", logger);
            await this.RunAndLogAsync("sudo mount " + device + " \"" + mountPoint + "\"", logger, false, 5);
            await this.RunAndLogAsync("sudo chown -R " + user + "." + user + " \"" + mountPoint + "\"", logger);

            await this.setupCmdLock.WaitAsync();
            {
                await this.RunAndLogStreamAsync("[ -x \"" + mountPoint + "/ec2manager/setup\" ] && \"" + mountPoint + "/ec2manager/setup\"", logger);
            }
            this.setupCmdLock.Release();
        }

        public IEnumerable<PortRangeDescription> GetPortDescriptions(string mountPointDir, ILogger logger)
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

        public string GetRunCommand(string mountPointDir, ILogger logger)
        {
            var mountPoint = this.mountBase + mountPointDir;

            return this.client.RunCommand("[ -r \"" + mountPoint + "/ec2manager/runcmd\" ] && cat \"" + mountPoint + "/ec2manager/runcmd\"").Result.Trim();
        }

        public bool IsCommandSessionStarted(string sessionName)
        {
            return this.client.RunCommand("screen -ls | grep " + sessionName).ExitStatus == 0;
        }

        public async Task RunCommandAsync(string from, string command, string sessionName, ILogger logger, CancellationToken? cancellationToken = null)
        {
            var cmd = "cd \"" + from + "\" && " + command + "";
            await this.RunInShellAsync(cmd, sessionName, logger, cancellationToken);
        }

        public async Task ResumeSessionAsync(string sessionName, ILogger logger, CancellationToken? cancellationToken = null)
        {
            await this.RunInShellAsync(null, sessionName, logger, cancellationToken);
        }

        public string GetUserInstruction(string mountPointDir, ILogger logger)
        {
            var mountPoint = this.mountBase + mountPointDir;

            return this.client.RunCommand("[ -r \"" + mountPoint + "/ec2manager/user_instruction\" ] && cat \"" + mountPoint + "/ec2manager/user_instruction\"").Result.Trim();
        }

        public string GetUptime()
        {
            return this.client.RunCommand("uptime").Result.Trim();
        }

        public string[] ListScripts(string mountPointDir)
        {
            var mountPoint = this.mountBase + mountPointDir;

            var lsOutput = this.client.RunCommand("[ -d \"" + mountPoint + "/ec2manager/scripts\" ] && ls -m \"" + mountPoint + "/ec2manager/scripts\"").Result.Trim();
            return lsOutput.Split(new[] { ", " }, StringSplitOptions.RemoveEmptyEntries);
        }

        public ScriptArgument[] GetScriptArguments(string mountPointDir, string script)
        {
            var mountPoint = this.mountBase + mountPointDir;

            var output = this.client.RunCommand("[ -f \"" + mountPoint + "/ec2manager/scripts/" + script + "\" ] && \"" + mountPoint + "/ec2manager/scripts/" + script + "\" --args").Result.Trim();
            var lines = output.Split(new[] { "\n", "\r\n" }, StringSplitOptions.RemoveEmptyEntries);
            return lines.Select(line =>
                {
                    var parts = line.Split(new[] { '\t' });
                    if (scriptArgumentTypeMapping.ContainsKey(parts[0]))
                        throw new Exception("Script return argument type " + parts[0] + ", which we don't know how to handle");
                    return new ScriptArgument(scriptArgumentTypeMapping[parts[0]], parts[1]);
                }).ToArray();
        }

        public async Task RunScriptAsync(string mountPointDir, string script, string[] args, ILogger logger)
        {
            var mountPoint = this.mountBase + mountPointDir;

            await this.RunAndLogStreamAsync("echo \"" + mountPoint + "/ec2manager/scripts/" + script + "\" " + string.Join(" ", args.Select(x => "\"" + x + "\"")), logger, true);
        }

        private async Task RunAndLogAsync(string command, ILogger logger, bool logResult = false, int retryTimes = 0)
        {
            logger.Log(command);
            var cmd = this.client.RunCommand(command);

            for (int i = 0; ; i++)
            {
                if (cmd.ExitStatus == 0)
                    break;

                logger.Log("Error: {0}. Retrying in 5 seconds", cmd.Error);
                if (i == retryTimes)
                    throw new Exception(string.Format("Command {0} failed with error {1}", command, cmd.Error));
                await Task.Delay(5000);
            }

            if (logResult)
                logger.Log(cmd.Result.TrimEnd());
        }

        private Task RunAndLogStreamAsync(string command, ILogger logger, bool logCommand = false)
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

        /// <summary>
        /// Note unintuitive behaviour - if command is null, tries to reconnect to session
        /// </summary>
        private Task RunInShellAsync(string command, string sessionName, ILogger logger, System.Threading.CancellationToken? cancellationToken = null)
        {
            System.Action runAction = () =>
                {
                    using (var stream = this.client.CreateShellStream("xterm", 80, 24, 800, 600, 1024))
                    {
                        var reader = new StreamReader(stream);
                        var writer = new StreamWriter(stream);
                        writer.AutoFlush = true;

                        reader.ReadToEnd();
                        if (command != null)
                        {
                            // Start a new screen session, with A as the control character (as we can't send meta-characters like ctrl)
                            writer.WriteLine("screen -eAa -S " + sessionName);
                            reader.ReadToEnd();
                            writer.WriteLine(command);
                        }
                        else
                        {
                            writer.WriteLine("screen -d -r " + sessionName);
                        }

                        while (true)
                        {
                            // Command is cancelled by stream being disposed
                            if (cancellationToken.HasValue && cancellationToken.Value.IsCancellationRequested)
                            {
                                writer.WriteLine("A:kill");
                                cancellationToken.Value.ThrowIfCancellationRequested();
                            }

                            var result = reader.ReadLine();
                            if (!string.IsNullOrEmpty(result))
                            {
                                result = result.TrimEnd().StripColors();
                                if (!string.IsNullOrWhiteSpace(result))
                                    logger.Log(result);
                            }
                            else
                            {
                                System.Threading.Thread.Sleep(100);
                            }
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

    public struct ScriptArgument
    {
        public Type Type;
        public string Description;

        public ScriptArgument (Type type, string description)
	    {
            this.Type = type;
            this.Description = description;
	    }
    }
}
