using Caliburn.Micro;
using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Ec2Manager.ViewModels
{
    [Export]
    [PartCreationPolicy(CreationPolicy.NonShared)]
    public class InstanceDetailsViewModel : Screen
    {
        public Logger Logger { get; set; }

        private StringBuilder logMessages = new StringBuilder();
        public string LogMessages
        {
            get { return this.logMessages.ToString(); }
        }

        [ImportingConstructor]
        public InstanceDetailsViewModel()
        {
            this.DisplayName = "Instance";
        }

        protected override void OnInitialize()
        {
            base.OnInitialize();

            this.Logger.NewLogEntry += (o, e) =>
                {
                    this.logMessages.AppendLine(e.Text);
                    this.NotifyOfPropertyChange(() => LogMessages);
                };
        }
    }
}
