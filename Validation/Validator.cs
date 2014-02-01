using Caliburn.Micro;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Threading.Tasks;

namespace Ec2Manager.Validation
{
    /// <summary>
    /// Validating member. It's recommended that you implement IValidationProvider yourself, which wraps this
    /// </summary>
    public class Validator : PropertyChangedBase, IValidator
    {
        private ConcurrentDictionary<string, PropertyValidator> propertyValidators = new ConcurrentDictionary<string, PropertyValidator>();

        /// <summary>
        /// Fired whenever a set of errors occur. Could be quite frequent!
        /// </summary>
        public event EventHandler<ErrorEventArgs> OnError;

        public Validator()
        {
        }

        /// <summary>
        /// Check an individual, named property for errors. Wrap with IDataErrorInfo.this[string]
        /// </summary>
        /// <param name="propertyName">Property name to check</param>
        /// <returns>Array of errors</returns>
        public string[] CheckProperty(string propertyName)
        {
            return this.CheckProperty(propertyName, false);
        }

        /// <summary>
        /// /// Check an individual property for errors. Wrap with IDataErrorInfo.this[string]
        /// </summary>
        /// <param name="property">Expression identifying the property to check</param>
        /// <returns>Array of errors</returns>
        public string[] CheckProperty<TProperty>(Expression<Func<TProperty>> property)
        {
            return this.CheckProperty(this.ExtractName(property));
        }

        /// <summary>
        /// Same as CheckProperty, but suppress the OnError event and INotifyPropertyChanged for HasErrors
        /// </summary>
        /// <param name="propertyName">Property name to check</param>
        /// <returns>Array of errors</returns>
        public string[] CheckPropertyWithoutNotifications(string propertyName)
        {
            return this.CheckProperty(propertyName, true);
        }

        /// <summary>
        /// Same as CheckProperty, but suppress the OnError event and INotifyPropertyChanged for HasErrors
        /// </summary>
        /// <param name="propertyName">Property name to check</param>
        /// <returns>Array of errors</returns>
        public string[] CheckPropertyWithoutNotifications<TProperty>(Expression<Func<TProperty>> property)
        {
            return this.CheckProperty(this.ExtractName(property), true);
        }

        private string[] CheckProperty(string propertyName, bool suppressNotifications)
        {
            PropertyValidator validator;
            if (this.propertyValidators.TryGetValue(propertyName, out validator))
            {
                var errors = validator.Validate();

                if (!suppressNotifications)
                {
                    this.FireOnError(errors);
                    this.NotifyOfPropertyChange(() => this.HasErrors);
                    this.NotifyOfPropertyChange(() => this.Errors);
                }

                return errors;
            }
            else
            {
                return new string[0];
            }
        }

        /// <summary>
        /// Check whether a named property is checked
        /// </summary>
        /// <param name="propertyName">Name of property</param
        public bool IsPropertyChecked(string propertyName)
        {
            return this.propertyValidators.ContainsKey(propertyName);
        }

        /// <summary>
        /// Checked whether a given property is checked
        /// </summary>
        /// <param name="property">Expression identifying property</param>
        /// <returns></returns>
        public bool IsPropertyChecked<TProperty>(Expression<Func<TProperty>> property)
        {
            return this.IsPropertyChecked(this.ExtractName(property));
        }

        /// <summary>
        /// Check all properties for errors. This differs from this.Errors in that this.Errors returns cached errors, whereas this re-checks all
        /// </summary>
        /// <returns>Array of errors</returns>
        public string[] CheckAllProperties()
        {
            var errors = this.propertyValidators.Keys.SelectMany(key => this.CheckProperty(key, false)).ToArray();

            this.FireOnError(errors);
            this.NotifyOfPropertyChange(() => this.HasErrors);
            this.NotifyOfPropertyChange(() => this.Errors);

            return errors;
        }

        /// <summary>
        /// Return cached validation errors. Wrap with IDataErrorInfo.Error
        /// </summary>
        public string[] Errors
        {
            get { return this.propertyValidators.Values.SelectMany(x => x.ResultCache).ToArray(); }
        }

        /// <summary>
        /// True if any properties report errors
        /// </summary>
        public bool HasErrors
        {
            get { return this.propertyValidators.Values.Any(x => x.ResultCache.Length > 0); }
        }

        /// <summary>
        /// Base function for validators. Wrap with extension methods in IValidationProvider
        /// </summary>
        /// <param name="property">Expression identifying property to validate</param>
        /// <param name="validator">Delegate accepting property value, and returning false if it fails validation</param>
        /// <param name="message">Message to return on validation failure. {0} is replaced with property name, {1} with its value</param>
        /// <returns></returns>
        public IValidation ValidateWith<TProperty>(Expression<Func<TProperty>> property, Func<TProperty, bool> validator, string message)
        {
            var name = this.ExtractName(property);

            var compiled = property.Compile();
            var validation = new Validation(name, () => compiled(), val => validator((TProperty)val), message);

            // Validations are never removed, so this is safe
            this.propertyValidators.TryAdd(name, new PropertyValidator());
            this.propertyValidators[name].AddValidation(validation);

            return validation;
        }

        /// <summary>
        /// Register a callback to be called when a specific property has errors
        /// </summary>
        /// <param name="propertyName">Name of property</param>
        /// <param name="onError">Callback to call</param>
        public void OnColumnError(string propertyName, Action<string[]> onError)
        {
            this.propertyValidators.TryAdd(propertyName, new PropertyValidator());
            this.propertyValidators[propertyName].OnError = onError;
        }

        /// <summary>
        /// Register a callback to be called when a specific property has errors
        /// </summary>
        /// <param name="property">Expression identifying property</param>
        /// <param name="onError">Callback to call</param>
        public void OnColumnError<TProperty>(Expression<Func<TProperty>> property, Action<string[]> onError)
        {
            this.OnColumnError(this.ExtractName(property), onError);
        }

        /// <summary>
        /// Remove all validators on all properties
        /// </summary>
        public void Clear()
        {
            foreach (var validator in this.propertyValidators.Values)
            {
                validator.ClearValidations();
            }
        }

        private string ExtractName<TProperty>(Expression<Func<TProperty>> property)
        {
            var body = property.Body as MemberExpression;
            if (body == null)
                throw new ArgumentException("not a MemberExpression", "property");

            return body.Member.Name;
        }

        private void FireOnError(string[] errors)
        {
            if (errors.Length == 0)
                return;

            var handler = this.OnError;
            if (handler != null)
            {
                handler(this, new ErrorEventArgs(errors));
            }
        }

        private class PropertyValidator
        {
            private object validationsLock = new object();
            private List<Validation> validations;

            public IReadOnlyList<Validation> Validations
            {
                get { return this.validations.AsReadOnly(); }
            }
            // True if the last validation passed successfully
            public string[] ResultCache { get; private set; }
            public Action<string[]> OnError { get; set; }

            public PropertyValidator()
            {
                this.validations = new List<Validation>();
                this.ResultCache = new string[0];
            }

            public string[] Validate()
            {
                // Current behaviour is to stop on the first failure

                string error;
                lock (this.validationsLock)
                {
                    error = this.validations.Select(x => x.Validate()).FirstOrDefault(x => !String.IsNullOrEmpty(x));
                }
                var errors = (error == null) ? new string[0] : new[] { error };
                this.ResultCache = errors;

                var onError = this.OnError;
                if (errors.Length > 0 && onError != null)
                    onError(errors);

                return errors;
            }

            public void AddValidation(Validation validation)
            {
                lock (this.validationsLock)
                {
                    this.validations.Add(validation);
                }
            }

            public void ClearValidations()
            {
                lock (this.validationsLock)
                {
                    this.validations.Clear();
                }
            }
        }

        private class Validation : IValidation
        {
            private string propertyName;
            private string message;
            private Func<bool> condition;
            private bool testNull = true;
            private Func<object> propertySelector;
            public Func<object, bool> Test { get; private set; }

            public Validation(string propertyName, Func<object> propertySelector, Func<object, bool> test, string defaultMessage)
            {
                this.propertyName = propertyName;
                this.propertySelector = propertySelector;
                this.Test = test;
                this.message = defaultMessage;
            }

            public string Validate()
            {
                object val = this.propertySelector();
                var condition = this.condition;
                if ((val != null || this.testNull) && (condition == null || condition()))
                    return this.Test(val) ? null : String.Format(this.message, this.propertyName, val);
                else
                    return null;
            }

            public IValidation WithMessage(string message)
            {
                this.message = message;
                return this;
            }

            public IValidation When(Func<bool> condition)
            {
                this.condition = condition;
                return this;
            }

            public IValidation TestNull(bool testNull)
            {
                this.testNull = testNull;
                return this;
            }
        }
    }

    public class ErrorEventArgs : EventArgs
    {
        public string[] Errors { get; private set; }

        public ErrorEventArgs(string[] errors)
            : base()
        {
            this.Errors = errors;
        }
    }

    public interface IValidator : IValidationProvider, INotifyPropertyChanged
    {
        event EventHandler<ErrorEventArgs> OnError;
        string[] CheckProperty(string propertyName);
        string[] CheckProperty<TProperty>(Expression<Func<TProperty>> property);
        string[] CheckPropertyWithoutNotifications(string propertyName);
        string[] CheckPropertyWithoutNotifications<TProperty>(Expression<Func<TProperty>> property);
        bool IsPropertyChecked(string propertyName);
        bool IsPropertyChecked<TProperty>(Expression<Func<TProperty>> property);
        string[] CheckAllProperties();
        string[] Errors { get; }
        bool HasErrors { get; }
        void OnColumnError(string propertyName, Action<string[]> onError);
        void OnColumnError<TProperty>(Expression<Func<TProperty>> property, Action<string[]> onError);
        void Clear();
    }

    public interface IValidationProvider
    {
        IValidation ValidateWith<TProperty>(Expression<Func<TProperty>> property, Func<TProperty, bool> validator, string message);
    }

    public interface IValidation
    {
        /// <summary>
        /// Provide a custom message for when the validation failes
        /// </summary>
        /// <param name="message">Message to display. {0} is replaced with property name, {1} with its value</param>
        IValidation WithMessage(string message);

        /// <summary>
        /// Add a condition for property validation evaluation
        /// </summary>
        /// <param name="condition">Validation isn't evaluated if condition returns true - automatically passes</param>
        IValidation When(Func<bool> condition);

        /// <summary>
        /// Validations are run for null values by default. Change this behaviour
        /// </summary>
        /// <param name="testNull">False to automatically pass null values</param>
        IValidation TestNull(bool testNull);
    }

    public static class ValidationExtensions
    {
        public static IValidation ValidateNotNull(this IValidationProvider validator, Expression<Func<string>> property)
        {
            return validator.ValidateWith(property, val => val != null, "{0} is null");
        }

        public static IValidation ValidateLength(this IValidationProvider validator, Expression<Func<string>> property, int minLength, int maxLength)
        {
            return validator.ValidateWith(property, val => val.Length >= minLength && val.Length <= maxLength, "{0} is the wrong length").TestNull(false);
        }

        public static IValidation ValidateRange(this IValidationProvider validator, Expression<Func<int>> property, int min, int max)
        {
            return validator.ValidateWith(property, val => val >= min && val <= max, "{0} must be between " + min + " and " + max);
        }

        public static IValidation ValidateNullRange(this IValidationProvider validator, Expression<Func<int?>> property, int min, int max)
        {
            return validator.ValidateWith(property, val => val != null && val >= min && val <= max, "{0} must be between " + min + " and " + max);
        }
    }
}
