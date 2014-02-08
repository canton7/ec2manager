using Caliburn.Micro;
using Ec2Manager.Classes;
using Ec2Manager.Utilities;
using Renci.SshNet;
using Renci.SshNet.Common;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Ec2Manager
{
    public class InstanceClient : PropertyChangedBase, IDisposable, IMachineInteractionProvider
    {
        private SshClient client;
        public string Host { get; private set; }
        public string User { get; private set; }
        public string Key { get; private set; }
        private SemaphoreSlim setupCmdLock = new SemaphoreSlim(1, 1);
        private SemaphoreSlim connectLock = new SemaphoreSlim(1, 1);

        public static readonly Dictionary<string, ScriptArgumentType> scriptArgumentTypeMapping = new Dictionary<string, ScriptArgumentType>()
        {
            { "string", ScriptArgumentType.String },
            { "bool",   ScriptArgumentType.Bool },
            { "enum",   ScriptArgumentType.Enum },
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
            get { return "/home/" + this.User + "/"; }
        }

        private const int Retries = 10;

        public InstanceClient(string host, string user, string key)
        {
            this.Host = host;
            this.User = user;
            this.Key = key;
        }

        /// <summary>
        /// Largely synchronous, unfortunately
        /// </summary>
        public async Task ConnectAsync(ILogger logger)
        {
            logger = logger ?? new StubLogger();

            await this.connectLock.WaitAsync();
            {
                logger.Log("Establishing connection with {0}@{1}", this.User, this.Host);

                if (this.client == null)
                    this.client = new SshClient(this.Host, this.User, new PrivateKeyFile(new MemoryStream(Encoding.ASCII.GetBytes(this.Key))));

                Exception lastException = null;

                for (int i = 0; i < Retries && !this.client.IsConnected; i++)
                {
                    try
                    {
                        await Task.Run(() => this.client.Connect());
                    }
                    catch (SshAuthenticationException e)
                    {
                        var msg = "SSH Authentication error:\n" + e.Format() + "\nMake sure you entered the right SSH user";
                        logger.Log(msg);
                        throw new Exception(msg, e);
                    }
                    catch (System.Net.Sockets.SocketException e)
                    {
                        lastException = e;
                    }
                    catch (SshException e)
                    {
                        lastException = e;
                    }

                    if (!this.client.IsConnected)
                        logger.Log("Connection attempt failed. Retrying...");

                    await Task.Delay(5000);
                }

                if (this.client.IsConnected)
                {
                    this.IsConnected = true;
                    logger.Log("Connected");
                }
                else
                {
                    var msg = "SSH connection failure: " + lastException.Message;
                    logger.Log(msg);
                    throw new Exception(msg, lastException);
                }
            }
            this.connectLock.Release();
        }

        public async Task DisconnectAsync()
        {
            await this.connectLock.WaitAsync();
            {
                if (this.client != null)
                    this.client.Disconnect();

                this.IsConnected = false;
            }
            this.connectLock.Release();
        }

        public async Task SetupFilesystemAsync(string device, ILogger logger)
        {
            await this.RunAndLogAsync("sudo mkfs.ext4 " + device, logger: logger, logResult: true);
        }

        public async Task MountDeviceAsync(string device, string mountPointDir, ILogger logger, CancellationToken? cancellationToken = null)
        {
            var mountPoint = this.mountBase + mountPointDir;

            await this.RunAndLogAsync("sudo mkdir -p \"" + mountPoint + "\"", logger, cancellationToken: cancellationToken);
            await this.RunAndLogAsync("sudo mount " + device + " \"" + mountPoint + "\"", logger, logResult: false, cancellationToken: cancellationToken);
            await this.RunAndLogAsync("sudo chown -R " + User + "." + User + " \"" + mountPoint + "\"", logger, cancellationToken: cancellationToken);
        }

        public async Task SetupDeviceAsync(string device, string mountPointDir, ILogger logger)
        {
            var mountPoint = this.mountBase + mountPointDir;

            await this.setupCmdLock.WaitAsync();
            {
                await this.RunAndLogStreamAsync("[ -x \"" + mountPoint + "/ec2manager/setup\" ] && \"" + mountPoint + "/ec2manager/setup\"", logger);
            }
            this.setupCmdLock.Release();
        }

        public async Task<IEnumerable<PortRangeDescription>> GetPortDescriptionsAsync(string mountPointDir, ILogger logger, CancellationToken? cancellationToken = null)
        {
            var mountPoint = this.mountBase + mountPointDir;

            var cmd = await this.RunAndLogAsync("[ -r \"" + mountPoint + "/ec2manager/ports\" ] && cat \"" + mountPoint + "/ec2manager/ports\"", checkExitStatus: false, cancellationToken: cancellationToken);
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

        public async Task<IEnumerable<LabelledValue>> GetRunCommandsAsync(string mountPointDir, CancellationToken? cancellationToken = null)
        {
            var mountPoint = this.mountBase + mountPointDir;

            var contents = (await this.RunAndLogAsync("[ -r \"" + mountPoint + "/ec2manager/runcmd\" ] && cat \"" + mountPoint + "/ec2manager/runcmd\"", checkExitStatus: false, cancellationToken: cancellationToken)).Result.Trim();
            var lines = contents.Split(new[] { "\n", "\r\n" }, StringSplitOptions.RemoveEmptyEntries);

            if (lines.Length == 0)
                return Enumerable.Empty<LabelledValue>();

            // Is it an old-school run command (just the command on its own?)
            if (lines.Length == 1 && !lines[0].Contains("\n"))
            {
                return new[] { new LabelledValue("Default Command", lines[0]) };
            }

            return lines.Select(entry =>
                {
                    var parts = entry.Split(new[] { '\t' }, 2);
                    return new LabelledValue(parts[0], parts[1]);
                });
        }

        public async Task<bool> IsCommandSessionStartedAsync(string sessionName, CancellationToken? cancellationToken = null)
        {
            return (await this.RunAndLogAsync("screen -ls | grep " + sessionName, checkExitStatus: false, cancellationToken: cancellationToken)).ExitStatus == 0;
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

        public async Task<string> GetUserInstructionAsync(string mountPointDir, CancellationToken? cancellationToken = null)
        {
            var mountPoint = this.mountBase + mountPointDir;

            return (await this.RunAndLogAsync("[ -r \"" + mountPoint + "/ec2manager/user_instruction\" ] && cat \"" + mountPoint + "/ec2manager/user_instruction\"", checkExitStatus: false, cancellationToken: cancellationToken)).Result.Trim();
        }

        public async Task<string> GetUptimeAsync(ILogger logger, CancellationToken? cancellationToken = null)
        {
            return (await this.RunAndLogAsync("uptime", logger: logger, logCommand: false, cancellationToken: cancellationToken)).Result.Trim();
        }

        public async Task<string[]> ListScriptsAsync(string mountPointDir, CancellationToken? cancellationToken = null)
        {
            var mountPoint = this.mountBase + mountPointDir;

            var lsOutput = (await this.RunAndLogAsync("[ -d \"" + mountPoint + "/ec2manager/scripts\" ] && find \"" + mountPoint + "/ec2manager/scripts\" -perm /u=x -type f -printf \"%f\n\"", checkExitStatus: false,  cancellationToken: cancellationToken)).Result.Trim();
            return lsOutput.Split(new[] { "\n" }, StringSplitOptions.RemoveEmptyEntries);
        }

        public async Task<ScriptArgument[]> GetScriptArgumentsAsync(string mountPointDir, string script, CancellationToken? cancellationToken = null)
        {
            var mountPoint = this.mountBase + mountPointDir;

            var output = (await this.RunAndLogAsync("[ -f \"" + mountPoint + "/ec2manager/scripts/" + script + "\" ] && \"" + mountPoint + "/ec2manager/scripts/" + script + "\" --args", checkExitStatus: false, cancellationToken: cancellationToken)).Result.Trim();
            var lines = output.Split(new[] { "\n", "\r\n" }, StringSplitOptions.RemoveEmptyEntries);
            return lines.Select(line =>
                {
                    var parts = line.Split(new[] { '\t' });

                    var match = Regex.Match(parts[0], @"^(\w+)(?:\[(.*)\])?$");
                    var typeKey = match.Groups[1].Value;
                    var args = match.Groups[2].Value;

                    if (!scriptArgumentTypeMapping.ContainsKey(typeKey))
                        throw new Exception("Script return argument type " + typeKey + ", which we don't know how to handle");

                    var argumentType = scriptArgumentTypeMapping[typeKey];

                    return new ScriptArgument(parts[1], parts[2], argumentType, string.IsNullOrEmpty(args) ? new string[0] : args.Split(new[]{ ',' }).ToArray());
                }).ToArray();
        }

        public async Task RunScriptAsync(string mountPointDir, string script, string[] args, ILogger logger, CancellationToken? cancellationToken = null)
        {
            var mountPoint = this.mountBase + mountPointDir;

            logger.Log("Starting script '{0}'", script);
            await this.RunAndLogStreamAsync(String.Format("cd \"{0}\" && \"ec2manager/scripts/{1}\" {2}", mountPoint, script, String.Join(" ", args.Select(x => "\"" + x + "\""))), logger, false, cancellationToken);

            logger.Log("Script finished");
        }

        private async Task<SshCommand> RunCommandAsync(string command, ILogger logger, System.Func<SshCommand, IAsyncResult, Task> doWithResult = null, CancellationToken? cancellationToken = null)
        {
            CancellationToken token = cancellationToken ?? new CancellationToken();

            SshCommand cmd = null;
            Exception lastException = null;

            for (int i = 0; i < Retries; i++)
            {
                try
                {
                    cmd = this.client.CreateCommand(command);
                    var tcs = new TaskCompletionSource<bool>();

                    var result = cmd.BeginExecute((asr) =>
                        {
                            if (asr.IsCompleted)
                                tcs.SetResult(true);
                        });

                    using (var registration = token.Register(() =>
                    {
                        cmd.CancelAsync();
                        tcs.TrySetResult(false);
                    }))
                    {
                        var tasks = new List<Task>();

                        if (doWithResult != null)
                            tasks.Add(doWithResult(cmd, result));

                        tasks.Add(tcs.Task);

                        await Task.WhenAll(tasks);
                    };

                    cmd.EndExecute(result);
                    lastException = null;

                    break;
                }
                catch (SocketException e)
                {
                    if (logger != null)
                        logger.Log("SocketException:\n" + e.Format());
                    lastException = e;
                }
                catch (SshException e)
                {
                    if (logger != null)
                        logger.Log("SshException:\n" + e.Format());
                    lastException = e;
                }

                this.IsConnected = false;

                if (logger != null)
                    logger.Log("Trying to re-establish connection");

                await this.ConnectAsync(logger);
            }

            if (lastException != null)
                throw lastException;

            return cmd;
        }

        private async Task<SshCommand> RunAndLogAsync(string command, ILogger logger = null, bool logCommand = true, bool logResult = false, bool checkExitStatus = true, CancellationToken? cancellationToken = null)
        {
            CancellationToken token = cancellationToken.HasValue ? cancellationToken.Value : new CancellationToken();

            if (logger != null && logCommand)
                logger.Log(command);

            var cmd = await this.RunCommandAsync(command, logger, null, token);

            if (checkExitStatus && cmd.ExitStatus > 0)
            {
                throw new Exception(string.Format("Command {0} failed with error {1}", command, cmd.Error));
            }

            if (logger != null && logResult)
                logger.Log(cmd.Result.TrimEnd());

            return cmd;
        }

        private Task RunAndLogStreamAsync(string command, ILogger logger, bool logCommand = false, CancellationToken? cancellationToken = null)
        {
            CancellationToken token = cancellationToken.HasValue ? cancellationToken.Value : new CancellationToken();

            if (logCommand)
                logger.Log(command);

            return Task.Run(async () =>
            {
                await this.RunCommandAsync(command, logger, async (cmd, result) =>
                    {
                        try
                        {
                            await logger.LogFromStream(result, cmd.OutputStream, cmd.ExtendedOutputStream, token);
                        }
                        catch (OperationCanceledException)
                        {
                            cmd.CancelAsync();
                            throw;
                        }
                    }, token);
            }, token);
        }

        /// <summary>
        /// Note unintuitive behaviour - if command is null, tries to reconnect to session
        /// </summary>
        private Task RunInShellAsync(string command, string sessionName, ILogger logger, CancellationToken? cancellationToken = null)
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
                            writer.WriteLine("screen -e'$a' -S " + sessionName);
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
                                writer.WriteLine("$:kill");
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
            this.DisconnectAsync().Wait();
        }
    }
}
