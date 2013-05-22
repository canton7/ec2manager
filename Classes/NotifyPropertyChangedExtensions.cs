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
        public static PropertyChangedEventHandler Bind<T, TR>(this T cls, Expression<Func<T, TR>> property, PropertyChangedEventHandler handler) where T : INotifyPropertyChanged
        {
            var body = property.Body as MemberExpression;

            if (body == null)
                throw new ArgumentException("Not MemberExpression", "property");

            PropertyChangedEventHandler ourHandler = (o, e) =>
            {
                if (e.PropertyName == body.Member.Name)
                    handler(o, e);
            };

            cls.PropertyChanged += ourHandler;

            return ourHandler;
        }

        public static void Unbind(this INotifyPropertyChanged cls, PropertyChangedEventHandler handler)
        {
            cls.PropertyChanged -= handler;
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
}
