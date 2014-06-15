using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Ec2Manager.Classes
{
    public static class ExceptionExtensions
    {
        public static string Format(this Exception e, int indentation = 0)
        {
            var sb = new StringBuilder();
            var aggregate = e as AggregateException;
            if (aggregate != null)
            {
                for (int i = 0; i < aggregate.InnerExceptions.Count; i++)
                {
                    sb.AppendFormat("Exception {0}:\n{1}", i + 1, IndentString(aggregate.InnerExceptions[0].Format(), indentation + 3));
                }
            }
            else
            {
                sb.AppendFormat("Exception: {0}\n", e.GetType().Name);
                sb.AppendFormat("Message: {0}\n", e.Message);
                sb.AppendFormat("Source: {0}\n", e.Source);
                sb.AppendFormat("Target site: {0}\n", e.TargetSite);
                sb.AppendFormat("Stack trace: {0}\n", e.StackTrace);
                if (e.InnerException != null)
                    sb.AppendFormat("Inner Exception:\n{0}\n", e.InnerException.Format(indentation + 3));
            }
            return IndentString(sb.ToString(), indentation);
        }

        private static string IndentString(string str, int indent)
        {
            var padding = "".PadLeft(indent);
            return String.Join("\n", str.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None).Select(x => String.Format("{0}{1}", padding, x)));
        }

    }
}
