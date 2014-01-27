using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Ec2Manager.Configuration
{
    public class Friend
    {
        public string Name { get; private set; }
        public string UserId { get; private set; }

        public Friend(string userId, string name)
        {
            this.UserId = userId;
            this.Name = name;
        }
    }
}
