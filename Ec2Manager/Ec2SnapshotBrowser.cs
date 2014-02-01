using Amazon.EC2;
using Amazon.EC2.Model;
using Ec2Manager.Configuration;
using Ec2Manager.Properties;
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
            // We might have friends with duplicate IDs (oops)
            var friendsMap = friends.Select((x, i) => new { Item = x, Index = i }).GroupBy(x => x.Item.UserId).ToDictionary(x => x.Key, x => x.ToList());

            var result = await this.client.DescribeSnapshotsAsync(new DescribeSnapshotsRequest()
            {
                OwnerIds = friends.Select(x => x.UserId).ToList(),
            });

            return from snapshot in result.Snapshots
                   where snapshot.Description.StartsWith(Settings.Default.SnapshotPrefix)
                   let filteredDescription = snapshot.Description.Substring(Settings.Default.SnapshotPrefix.Length)
                   let mapItem = friendsMap.ContainsKey(snapshot.OwnerId) ? friendsMap[snapshot.OwnerId] : friendsMap["self"]
                   from friend in mapItem
                   orderby friend.Index, filteredDescription
                   select new Configuration.VolumeType(snapshot.SnapshotId, filteredDescription, friend.Item);
        }

        public async Task<int?> CountSnapshotsForUserId(string userId)
        {
            try
            {
                var results = await this.client.DescribeSnapshotsAsync(new DescribeSnapshotsRequest()
                {
                    OwnerIds = new List<string>() { userId },
                });
                return results.Snapshots.Count(x => x.Description.StartsWith(Settings.Default.SnapshotPrefix));
            }
            catch (AmazonEC2Exception)
            {
                return null;
            }
        }
    }
}
