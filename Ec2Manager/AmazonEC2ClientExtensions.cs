using Amazon.EC2;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Ec2Manager.Ec2Manager
{
    public static class AmazonEC2ClientExtensions
    {
        public static async Task UntilAsync(this AmazonEC2Client client, Func<AmazonEC2Client, Task<string>> stateMap, string desiredState, CancellationToken? cancellationToken = null)
        {
            bool gotToState = false;
            CancellationToken token = cancellationToken.HasValue ? cancellationToken.Value : new CancellationToken();

            while (!gotToState)
            {
                token.ThrowIfCancellationRequested();
                if (await stateMap(client) == desiredState)
                    gotToState = true;
                else
                    await Task.Delay(1000);
            }
        }
    }
}
