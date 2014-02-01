using Caliburn.Micro;
using Ec2Manager.Properties;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Serialization;

namespace Ec2Manager.Configuration
{
    [XmlRoot("MainConfig")]
    public class MainConfig : PropertyChangedBase
    {
        public MainConfig()
        {
            this.Friends = new List<Friend>();
            this.ShowOfficialImages = true;
        }

        public static MainConfig FromFile(string filename)
        {
            var serializer = new XmlSerializer(typeof(MainConfig));
            using (var sr = new StreamReader(filename))
            {
                return (MainConfig)serializer.Deserialize(sr);
            }
        }

        public void SaveToFile(string filename)
        {
            var serializer = new XmlSerializer(typeof(MainConfig));
            using (var sw = new StreamWriter(filename))
            {
                serializer.Serialize(sw, this);
            }
        }

        private string awsAccessKey;
        public string AwsAccessKey
        {
            get { return this.awsAccessKey; }
            set
            {
                this.awsAccessKey = value;
                this.NotifyOfPropertyChange();
            }
        }

        private string awsSecretKey;
        public string AwsSecretKey
        {
            get { return this.awsSecretKey; }
            set
            {
                this.awsSecretKey = value;
                this.NotifyOfPropertyChange();
            }
        }

        public string PuttyPath { get; set; }

        public List<Friend> Friends { get; set; }

        public bool ShowOfficialImages { get; set; }
    }
}
