using Amazon.EC2;
using Amazon.EC2.Model;
using Ec2Manager.Configuration;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Ec2Manager.Ec2Manager
{
    public class Ec2SnapshotBrowser
    {
        private AmazonEC2Client client;

        public Ec2SnapshotBrowser(AmazonEC2Client client)
        {
            this.client = client;
        }

        // Returns sorted
        public async Task<IEnumerable<Configuration.VolumeType>> GetSnapshotsForFriendsAsync(IEnumerable<Friend> friends)
        {
            var friendsMap = friends.Select((x, i) => new { Item = x, Index = i }).ToDictionary(x => x.Item.UserId, x => x);

            var result = await this.client.DescribeSnapshotsAsync(new DescribeSnapshotsRequest()
            {
                OwnerIds = friends.Select(x => x.UserId).ToList(),
                Filters = new List<Filter>()
                {
                    new Filter() { Name = "tag-key", Values = new List<string>() { "CreatedByEc2Manager" } },
                }
            });

            return from snapshot in result.Snapshots
                    let mapItem = friendsMap.ContainsKey(snapshot.OwnerId) ? friendsMap[snapshot.OwnerId] : friendsMap["self"]
                    orderby mapItem.Index, snapshot.Description
                    select new Configuration.VolumeType(snapshot.SnapshotId, snapshot.Description, mapItem.Item);
        }
    }
}
