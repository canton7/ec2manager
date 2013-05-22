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
            this.DefaultAmi = Settings.Default.DefaultAMI;
            this.DefaultLogonUser = Settings.Default.DefaultLogonUser;
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

        private string defaultAmi;
        public string DefaultAmi
        {
            get { return this.defaultAmi; }
            set
            {
                this.defaultAmi = value;
                this.NotifyOfPropertyChange();
            }
        }
        // If they don't change it, use the one from App.config. This allows us to change the default easily
        public bool ShouldSerializeDefaultAmi()
        {
            return this.DefaultAmi != Settings.Default.DefaultAMI;
        }

        public string DefaultLogonUser { get; set; }
        // Likewise
        public bool ShouldSerializeDefaultLogonUser()
        {
            return this.DefaultLogonUser != Settings.Default.DefaultLogonUser;
        }
    }
}
