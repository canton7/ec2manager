using Caliburn.Micro;
using Ec2Manager.Configuration;
using Ec2Manager.Validation;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Ec2Manager.ViewModels
{
    public class ManageFriendsViewModel : Screen
    {
        private Config config;

        public BindableCollection<FriendModel> Friends { get; private set; }

        public ManageFriendsViewModel(Config config)
        {
            this.DisplayName = "Manage Friends";

            this.config = config;
            this.Friends = new BindableCollection<FriendModel>(config.FriendsWithoutDefaults.Select(x => new FriendModel(x)));
        }

        
    }

    public class FriendModel : PropertyChangedBase, IDataErrorInfo, IValidationProvider
    {
        private IValidator validator = new Validator();

        public string Name { get; set; }
        public string UserId { get; set; }

        public FriendModel()
        {
            this.ValidateWith(() => this.Name, x => false, "Bad something or other");
        }

        public FriendModel(Friend friend) : this()
        {
            this.Name = friend.Name;
            this.UserId = friend.UserId;
        }

        public Friend FriendValue
        {
            get { return new Friend(this.UserId, this.Name); }
        }

        public IValidation ValidateWith<TProperty>(System.Linq.Expressions.Expression<Func<TProperty>> property, Func<TProperty, bool> validator, string message)
        {
            return this.validator.ValidateWith(property, validator, message);
        }

        public string Error
        {
            get { return String.Join(Environment.NewLine, this.validator.Errors); }
        }

        public string this[string columnName]
        {
            get
            {
                if (this.validator.IsPropertyChecked(columnName))
                {
                    var errors = String.Join(Environment.NewLine, this.validator.CheckProperty(columnName));
                    this.NotifyOfPropertyChange(() => this.Error);
                    return errors;
                }
                else
                {
                    return "";
                }
            }
        }
    }
}
