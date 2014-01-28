using Caliburn.Micro;
using Ec2Manager.Validation;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Ec2Manager.ViewModels
{
    public class EditFriendViewModel : Screen, IDataErrorInfo
    {
        private IValidator validator = new Validator();

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

        private string _userId;
        public string UserId
        {
            get { return this._userId; }
            set
            {
                this._userId = value;
                this.NotifyOfPropertyChange(() => this.CanSave);
            }
        }

        private string _name;
        public string Name
        {
            get { return this._name; }
            set
            {
                this._name = value;
                this.NotifyOfPropertyChange(() => this.CanSave);
            }
        }

        public EditFriendViewModel()
        {
            this.DisplayName = "Add or Edit friend";

            this.validator.ValidateWith(() => this.Name, x => !String.IsNullOrWhiteSpace(x), "Name must not be empty").TestNull(false);
            this.validator.ValidateWith(() => this.UserId, x => Regex.Match(x, @"^9\d{11}$").Success, "Bad Amazon User Id. Must be of the form 9xxxxxxxxxxx").TestNull(false);
        }

        public bool CanSave
        {
            get { return !String.IsNullOrWhiteSpace(this.UserId) && !String.IsNullOrWhiteSpace(this.Name) && !this.validator.HasErrors; }
        }

        public void Save()
        {
            this.TryClose();
        }
    }
}
