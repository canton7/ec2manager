using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Ec2Manager.Configuration
{
    public class Friend
    {
        public string Name { get; set; }
        public string UserId { get; set; }

        public Friend()
        {
        }

        public Friend(string userId, string name)
        {
            this.UserId = userId;
            this.Name = name;
        }
    }
}
