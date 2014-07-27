using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Serialization;

namespace Ec2Manager.Configuration
{
    internal class InstanceSizeConfigurationHandler : ConfigurationSection
    {
        private InstanceSizesConfiguration config;

        protected override void DeserializeSection(XmlReader reader)
        {
            var serializer = new XmlSerializer(typeof(InstanceSizesConfiguration), new XmlRootAttribute(this.SectionInformation.Name));
            this.config = (InstanceSizesConfiguration)serializer.Deserialize(reader);
        }

        protected override object GetRuntimeObject()
        {
            return this.config;
        }
    }

    public class InstanceSizesConfiguration
    {
        public static readonly InstanceSizesConfiguration Default = (InstanceSizesConfiguration)System.Configuration.ConfigurationManager.GetSection("instanceSizes");

        [XmlElement("instanceSize")]
        public List<InstanceSizeConfiguration> InstanceSizes { get; set; }
    }

    public class InstanceSizeConfiguration
    {
        [XmlAttribute("name")]
        public string Name { get; set; }

        [XmlAttribute("key")]
        public string Key { get; set; }
    }
}
