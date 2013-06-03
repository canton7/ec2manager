using Amazon.EC2;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Ec2Manager.Ec2Manager
{
    public class InstanceSpecification
    {
        public string Ami { get; private set; }
        public InstanceSize Size { get; private set; }
        public string AvailabilityZone { get; set; }
        public double? SpotBidPrice { get; private set; }

        public bool IsSpotInstance
        {
            get { return this.SpotBidPrice.HasValue; }
        }

        public InstanceSpecification(string instanceAmi, InstanceSize instanceSize, string availabilityZone = null, double? spotBidPrice = null)
        {
            this.Ami = instanceAmi;
            this.Size = instanceSize;
            this.AvailabilityZone = availabilityZone;
            this.SpotBidPrice = spotBidPrice;
        }
    }
}
