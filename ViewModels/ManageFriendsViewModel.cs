using Caliburn.Micro;
using Ec2Manager.Configuration;
using Ec2Manager.Validation;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Ec2Manager.Classes;
using Ec2Manager.Ec2Manager;

namespace Ec2Manager.ViewModels
{
    public class ManageFriendsViewModel : Screen
    {
        private IWindowManager windowManager;
        private Config config;
        private Ec2SnapshotBrowser snapshotBrowser;

        private string _ownUserId;
        public string OwnUserId
        {
            get { return this._ownUserId; }
            set
            {
                this._ownUserId = value;
                this.NotifyOfPropertyChange();
            }
        }

        private bool _showOfficialImages;
        public bool ShowOfficialImages
        {
            get { return this._showOfficialImages; }
            set
            {
                this._showOfficialImages = value;
                this.NotifyOfPropertyChange();
            }
        }

        public BindableCollection<FriendModel> Friends { get; private set; }

        private FriendModel _selectedFriend;
        public FriendModel SelectedFriend
        {
            get { return this._selectedFriend; }
            set
            {
                this._selectedFriend = value;
                this.NotifyOfPropertyChange();
            }
        }

        private PropertyChangedSubscription friendBeingEditedChangeHandler;
        private FriendModel _friendBeingEdited;
        public FriendModel FriendBeingEdited
        {
            get { return this._friendBeingEdited; }
            set
            {
                if (this._friendBeingEdited != null)
                    this._friendBeingEdited.Unbind(friendBeingEditedChangeHandler);
                
                this._friendBeingEdited = value;

                if (this._friendBeingEdited != null)
                {
                    this.friendBeingEditedChangeHandler = this._friendBeingEdited.Bind(x => x.Error, (o, e) =>
                        {
                            this.NotifyOfPropertyChange(() => this.CanAddFriend);
                            this.NotifyOfPropertyChange(() => this.CanEditFriend);
                            this.NotifyOfPropertyChange(() => this.CanSave);
                        });
                }

                this.NotifyOfPropertyChange();
            }
        }

        public ManageFriendsViewModel(IWindowManager windowManager, Config config, Ec2Connection connection)
        {
            this.DisplayName = "Manage Friends";

            this.windowManager = windowManager;
            this.config = config;
            this.snapshotBrowser = connection.CreateSnapshotBrowser();

            connection.GetUserIdAsync().ContinueWith(t => this.OwnUserId = t.Result);

            this.ShowOfficialImages = config.MainConfig.ShowOfficialImages;
            this.Friends = new BindableCollection<FriendModel>(config.FriendsWithoutDefaults.Select(x => new FriendModel(x, snapshotBrowser)));

            this.Bind(x => x.SelectedFriend, (o, e) => this.NotifyOfPropertyChange(() => this.CanEditFriend));
            this.Bind(x => x.SelectedFriend, (o, e) => this.NotifyOfPropertyChange(() => this.CanDeleteFriend));

            this.Bind(x => x.FriendBeingEdited, (o, e) => this.NotifyOfPropertyChange(() => this.CanAddFriend));
            this.Bind(x => x.FriendBeingEdited, (o, e) => this.NotifyOfPropertyChange(() => this.CanEditFriend));
            this.Bind(x => x.FriendBeingEdited, (o, e) => this.NotifyOfPropertyChange(() => this.CanSave));
        }

        public bool CanAddFriend
        {
            get { return this.FriendBeingEdited == null || this.FriendBeingEdited.Error == ""; }
        }
        public void AddFriend()
        {
            var friend = new FriendModel(this.snapshotBrowser);
            this.Friends.Add(friend);
            this.FriendBeingEdited = friend;
            this.SelectedFriend = friend;
        }

        public bool CanEditFriend
        {
            get { return this.SelectedFriend != null && (this.FriendBeingEdited == null || this.FriendBeingEdited.Error == "") && this.FriendBeingEdited != this.SelectedFriend; }
        }
        public void EditFriend()
        {
            this.FriendBeingEdited = this.SelectedFriend;
        }
        
        public bool CanDeleteFriend
        {
            get { return this.SelectedFriend != null; }
        }
        public void DeleteFriend()
        {
            if (this.FriendBeingEdited == this.SelectedFriend)
                this.FriendBeingEdited = null;
            this.Friends.Remove(this.SelectedFriend);
        }

        public bool CanSave
        {
            get { return this.FriendBeingEdited == null || this.FriendBeingEdited.Error == ""; }
        }
        public void Save()
        {
            this.config.MainConfig.ShowOfficialImages = this.ShowOfficialImages;
            this.config.FriendsWithoutDefaults = this.Friends.Select(x => x.FriendValue);
            this.config.SaveMainConfig();
            this.TryClose(true);
        }

        
    }

    public class FriendModel : PropertyChangedBase, IDataErrorInfo, IValidationProvider
    {
        private IValidator validator = new Validator();
        private Ec2SnapshotBrowser snapshotBrowser;

        private string _name;
        public string Name
        {
            get { return this._name; }
            set
            {
                this._name = value;
                this.NotifyOfPropertyChange();
            }
        }

        private string _userId;
        public string UserId
        {
            get { return this._userId; }
            set
            {
                this._userId = value;
                this.NotifyOfPropertyChange();
            }
        }

        private int? _numSnapshots;
        public int? NumSnapshots
        {
            get { return this._numSnapshots; }
            set
            {
                this._numSnapshots = value;
                this.NotifyOfPropertyChange();
            }
        }

        public FriendModel(Ec2SnapshotBrowser snapshotBrowser)
        {
            this.snapshotBrowser = snapshotBrowser;
            // TODO: For some reason this isn't always fired
            this.Bind(x => x.UserId, (o, e) =>
                {
                    if (this.validator.CheckPropertyWithoutNotifications(() => this.UserId).Length == 0)
                        this.snapshotBrowser.CountSnapshotsForUserId(this.UserId).ContinueWith(t => this.NumSnapshots = t.Result);
                    else
                        this.NumSnapshots = null;
                });

            this.ValidateWith(() => this.Name, x => !String.IsNullOrWhiteSpace(x), "Name must not be empty");
            this.ValidateWith(() => this.UserId, x => x != null && Regex.Match(x, @"^9\d{11}$").Success, "Bad Amazon User Id. Must be of the form 9xxxxxxxxxxx");
        }

        public FriendModel(Friend friend, Ec2SnapshotBrowser snapshotBrowser) : this(snapshotBrowser)
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
