using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Ec2Manager.Classes
{
    public static class NotifyPropertyChangedExtensions
    {
        /// <summary>
        /// Weakly bind to a property on an object which implements INotifyPropertyChanged, such that your handler is called every time the property changes.
        /// </summary>
        /// <typeparam name="TBindTo">Type of object to bind to (inferrable, you shouldn't need to specify this)</typeparam>
        /// <typeparam name="TBindType">Type of param to bind to (inferrable, you shouldn't need to specify this)</typeparam>
        /// <param name="cls">Object to bind to</param>
        /// <param name="selector">Expression describing parameter to bind to, e.g. obj => obj.MyParameter</param>
        /// <param name="handler">Action called whenever the parameter changes</param>
        /// <example>myObject.Bind(obj => obj.MyParameter, newval => this.someMethod(newval));</example>
        /// <returns>The handler to pass to Unbind, if you need it</returns>
        public static PropertyChangedSubscription Bind<TBindTo, TBindType>(this TBindTo cls, Expression<Func<TBindTo, TBindType>> selector, EventHandler<PropertyChangedEventArgs> handler) where TBindTo : INotifyPropertyChanged
        {

            var body = selector.Body as MemberExpression;

            if (body == null)
                throw new ArgumentException("Not MemberExpression", "property");

            var propertyName = body.Member.Name;

            //EventHandler<PropertyChangedEventArgs> ourHandler = (o, e) => handler(compiledSelector(cls));
            PropertyChangedEventManager.AddHandler(cls, handler, propertyName);

            return new PropertyChangedSubscription(propertyName, handler);
        }

        /// <summary>
        /// Unbind a handler returned by Bind
        /// </summary>
        /// <param name="cls"></param>
        /// <param name="handler"></param>
        public static void Unbind(this INotifyPropertyChanged cls, PropertyChangedSubscription subscription)
        {
            if (subscription != null)
            {
                PropertyChangedEventManager.RemoveHandler(cls, subscription.Handler, subscription.PropertyName);
            }
        }

        public static Task UntilAsync<T>(this T cls, Expression<Func<T, bool>> condition) where T : INotifyPropertyChanged
        {
            var body = condition.Body as BinaryExpression;
            
            if (body == null)
                throw new ArgumentException("Not BinaryExpression", "condition");

            var expressionStack = new Stack<BinaryExpression>();
            expressionStack.Push(body);
            var memberList = new List<MemberExpression>();

            while (expressionStack.Count > 0)
            {
                var item = expressionStack.Pop();

                if (item.Left is BinaryExpression)
                    expressionStack.Push((BinaryExpression)item.Left);
                else if (item.Left is MemberExpression)
                    memberList.Add((MemberExpression)item.Left);

                if (item.Right is BinaryExpression)
                    expressionStack.Push((BinaryExpression)item.Right);
                else if (item.Right is MemberExpression)
                    memberList.Add((MemberExpression)item.Right);
            }

            var compiledCondition = condition.Compile();
            var memberStringList = memberList.Select(x => x.Member.Name).ToArray();

            var tcs = new TaskCompletionSource<object>();

            PropertyChangedEventHandler handler = null;
            handler = (o, e) =>
                {
                    if (memberStringList.Contains(e.PropertyName) && compiledCondition(cls))
                    {
                        tcs.SetResult(null);
                        cls.PropertyChanged -= handler;
                    }
                };
            cls.PropertyChanged += handler;

            return tcs.Task;
        }
    }

    public class PropertyChangedSubscription
    {
        public string PropertyName { get; private set; }
        public EventHandler<PropertyChangedEventArgs> Handler { get; private set; }

        public PropertyChangedSubscription(string propertyName, EventHandler<PropertyChangedEventArgs> handler)
        {
            this.PropertyName = propertyName;
            this.Handler = handler;
        }
    }
}
