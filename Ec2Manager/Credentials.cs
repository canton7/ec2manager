using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Ec2Manager.Ec2Manager
{
    public class Credentials
    {
        public string AwsAccessKey;
        public string AwsSecretKey;

        public Credentials(string awsAccessKey, string awsSecretKey)
        {
            this.AwsAccessKey = awsAccessKey;
            this.AwsSecretKey = awsSecretKey;
        }

        public bool IsComplete
        {
            get { return !string.IsNullOrWhiteSpace(this.AwsAccessKey) && !string.IsNullOrWhiteSpace(this.AwsSecretKey); }
        }
    }
}
